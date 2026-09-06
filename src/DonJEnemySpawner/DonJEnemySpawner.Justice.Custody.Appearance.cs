using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Xml;
using GTA;
using GTA.Native;

internal sealed class JusticeClothingItem
{
    internal JusticeClothingItem(bool prop, int slot, int drawable, int texture, int palette)
    { Prop = prop; Slot = slot; Drawable = drawable; Texture = texture; Palette = palette; }
    internal bool Prop { get; }
    internal int Slot { get; }
    internal int Drawable { get; }
    internal int Texture { get; }
    internal int Palette { get; }
}

internal sealed class JusticeAppearancePersistenceSnapshot
{
    internal JusticeAppearancePersistenceSnapshot(string episodeId, int playerSlot, int modelHash,
        IEnumerable<JusticeClothingItem> items, bool restorePending = false)
    {
        EpisodeId = episodeId; PlayerSlot = playerSlot; ModelHash = modelHash;
        RestorePending = restorePending;
        Items = new ReadOnlyCollection<JusticeClothingItem>(new List<JusticeClothingItem>(items));
    }
    internal string EpisodeId { get; }
    internal int PlayerSlot { get; }
    internal int ModelHash { get; }
    internal bool RestorePending { get; }
    internal IReadOnlyList<JusticeClothingItem> Items { get; }
    internal JusticeAppearancePersistenceSnapshot ForRestore() =>
        new JusticeAppearancePersistenceSnapshot(EpisodeId, PlayerSlot, ModelHash, Items, true);
}

public sealed partial class DonJEnemySpawner
{
    private const int JusticeCustodyPersonalEffectsCheckMs = 1000;
    private const ulong JusticeNativeSetClothing = 0x262B14F48D29DE80UL;
    private const ulong JusticeNativeValidClothing = 0xE825F6B6CEA7671DUL;
    private const ulong JusticeNativeSetClothingProp = 0x93376B65A266EB5FUL;
    private const ulong JusticeNativeClearClothingProp = 0x0943E5B8E078E76EUL;
    private JusticeAppearancePersistenceSnapshot _justiceCustodyAppearance;
    private int _justiceNextCustodyAppearanceAt;
    private bool _justiceCustodyAppearanceSuppressed;
    private bool _justiceCustodyAppearanceRollbackPending;

    private static JusticeClothingItem[] GetJusticeCustodyUniform(int slot)
    {
        if (slot < 0 || slot > 2) return null;
        // Je reprends la combinaison « Navy Boiler Suit » du catalogue solo :
        // Michael 20, Franklin 13, Trevor 17 (fbi4_prep3, func_207/func_129).
        // Je ne touche jamais aux composants du visage, de la barbe ou des cheveux.
        int upper = slot == 0 ? 12 : slot == 1 ? 1 : 5;
        int lower = slot == 0 ? 11 : slot == 1 ? 1 : 5;
        int texture = slot == 2 ? 2 : 3;
        List<JusticeClothingItem> items = new List<JusticeClothingItem>(18);
        for (int component = 3; component <= 11; component++)
        {
            int drawable = component == 3 ? upper : component == 4 ? lower :
                component == 6 ? (slot == 2 ? 5 : 1) : component == 8 && slot != 0 ? 14 : 0;
            items.Add(new JusticeClothingItem(false, component, drawable,
                component == 3 || component == 4 ? texture : 0, 0));
        }
        for (int prop = 0; prop < 9; prop++)
            items.Add(new JusticeClothingItem(true, prop, -1, 0, 0));
        return items.ToArray();
    }

    private static JusticeClothingItem ReadJusticeClothing(Ped player, JusticeClothingItem item)
    {
        int drawable = Function.Call<int>((Hash)(item.Prop ? 0x898CC20EA75BACD8UL : 0x67F3780DD425D4FCUL),
            player.Handle, item.Slot);
        int texture = item.Prop && drawable == -1 ? 0 : Function.Call<int>(
            (Hash)(item.Prop ? 0xE131A28626F81AB2UL : 0x04A355E041E004E6UL), player.Handle, item.Slot);
        int palette = item.Prop ? 0 : Function.Call<int>((Hash)0xE3DD5F2A84B42281UL, player.Handle, item.Slot);
        if (drawable < (item.Prop ? -1 : 0) || drawable > 4095 || texture < 0 || texture > 4095 || palette < 0 || palette > 3)
            throw new InvalidOperationException("Composant de tenue illisible.");
        return new JusticeClothingItem(item.Prop, item.Slot, drawable, texture, palette);
    }

    private static bool SameJusticeClothing(JusticeClothingItem a, JusticeClothingItem b) =>
        a.Drawable == b.Drawable && a.Texture == b.Texture && a.Palette == b.Palette;

    private static bool WriteJusticeClothingIfChanged(Ped player, JusticeClothingItem item)
    {
        if (SameJusticeClothing(ReadJusticeClothing(player, item), item)) return true;
        if (!item.Prop)
            Function.Call((Hash)JusticeNativeSetClothing, player.Handle, item.Slot, item.Drawable, item.Texture, item.Palette);
        else if (item.Drawable < 0)
            Function.Call((Hash)JusticeNativeClearClothingProp, player.Handle, item.Slot);
        else
            Function.Call((Hash)JusticeNativeSetClothingProp, player.Handle, item.Slot, item.Drawable, item.Texture, true);
        return SameJusticeClothing(ReadJusticeClothing(player, item), item);
    }

    private bool IsJusticeAppearanceOwner(Ped player)
    {
        return _justiceCustodyAppearance != null && Entity.Exists(player) && !player.IsDead &&
            _justiceCustodyAppearance.PlayerSlot == _justiceActivePlayerProfileSlot &&
            GetJusticeCanonicalPlayerSlotSafe() == _justiceCustodyAppearance.PlayerSlot &&
            GetJusticePedModelHashSafe(player) == _justiceCustodyAppearance.ModelHash;
    }

    private void PrepareJusticeCustodyAppearance(Ped player)
    {
        if (_justiceCustodyAppearance != null || _justiceCustodyAppearanceSuppressed ||
            !Entity.Exists(player) || player.IsDead || _justiceCaseState == null ||
            string.IsNullOrEmpty(_justiceCaseState.CustodyEpisodeId) ||
            !IsJusticeCustodyPlayerIdentityCompatible(player)) return;
        int slot = GetJusticeCanonicalPlayerSlotSafe();
        JusticeClothingItem[] uniform = GetJusticeCustodyUniform(slot);
        if (uniform == null || slot != _justiceActivePlayerProfileSlot) return;
        int expectedModel = Game.GenerateHash(slot == 0 ? "player_zero" : slot == 1 ? "player_one" : "player_two");
        if (GetJusticePedModelHashSafe(player) != expectedModel) return;
        try
        {
            List<JusticeClothingItem> original = new List<JusticeClothingItem>(uniform.Length);
            foreach (JusticeClothingItem item in uniform)
            {
                if (!item.Prop && !Function.Call<bool>((Hash)JusticeNativeValidClothing,
                    new InputArgument[] { player.Handle, item.Slot, item.Drawable, item.Texture }))
                    throw new InvalidOperationException("Combinaison indisponible pour ce modèle.");
                original.Add(ReadJusticeClothing(player, item));
            }
            _justiceCustodyAppearance = new JusticeAppearancePersistenceSnapshot(
                _justiceCaseState.CustodyEpisodeId, slot, GetJusticePedModelHashSafe(player), original);
            // Je force la barrière existante avant la première modification physique.
            _justiceCustodyTransferPrecommitConfirmed = false;
            JusticeMarkStateDirty();
        }
        catch (Exception ex)
        {
            _justiceCustodyAppearanceSuppressed = true;
            LogException("Justice.Tenue.Capture", ex);
        }
    }

    private void ApplyJusticeCustodyAppearance(Ped player, int now)
    {
        if (!JusticeCustodyHasReached(now, _justiceNextCustodyAppearanceAt)) return;
        if (_justiceCustodyAppearanceRollbackPending && IsJusticeAppearanceOwner(player) &&
            JusticeCustodyHasReached(now, _justiceNextCustodyAppearanceAt))
        {
            _justiceNextCustodyAppearanceAt = JusticeCustodyFutureTime(now, JusticeCustodyPersonalEffectsCheckMs);
            _justiceCustodyAppearanceRollbackPending = !RestoreJusticeCustodyAppearance(player, true);
        }
        if (_justiceCustodyAppearanceSuppressed || !IsJusticeAppearanceOwner(player) ||
            _justiceCustodyAppearance.RestorePending ||
            (!_justiceCustodyTransferPrecommitConfirmed &&
                (!_justiceCustodyRuntimeActive || _justiceCustodyTransferPending || _justiceCustodyResumePending)) ||
            _justiceCaseState == null || _justiceCustodyAppearance.EpisodeId != _justiceCaseState.CustodyEpisodeId ||
            !JusticeCustodyHasReached(now, _justiceNextCustodyAppearanceAt)) return;
        _justiceNextCustodyAppearanceAt = JusticeCustodyFutureTime(now, JusticeCustodyPersonalEffectsCheckMs);
        try
        {
            foreach (JusticeClothingItem item in GetJusticeCustodyUniform(_justiceCustodyAppearance.PlayerSlot))
                if (!WriteJusticeClothingIfChanged(player, item))
                    throw new InvalidOperationException("Le jeu a refusé un composant de la combinaison.");
        }
        catch (Exception ex)
        {
            // Je restaure les seuls composants déjà changés et laisse l'admission
            // continuer : une panne cosmétique ne relance jamais le masque de transfert.
            _justiceCustodyAppearanceSuppressed = true;
            _justiceCustodyAppearanceRollbackPending = !RestoreJusticeCustodyAppearance(player, true);
            LogException("Justice.Tenue.Application", ex);
        }
    }

    private bool RestoreJusticeCustodyAppearance(Ped player, bool provisional = false)
    {
        if (_justiceCustodyAppearance == null) return true;
        if (!IsJusticeAppearanceOwner(player)) return false;
        if (!provisional && !_justiceCustodyAppearance.RestorePending)
        {
            _justiceCustodyAppearance = _justiceCustodyAppearance.ForRestore();
            JusticeMarkStateDirty();
        }
        bool restored = true;
        JusticeClothingItem[] uniform = GetJusticeCustodyUniform(_justiceCustodyAppearance.PlayerSlot);
        for (int index = 0; index < _justiceCustodyAppearance.Items.Count; index++)
        {
            try
            {
                JusticeClothingItem original = _justiceCustodyAppearance.Items[index];
                JusticeClothingItem current = ReadJusticeClothing(player, original);
                // Je préserve un vêtement choisi depuis une restitution partielle.
                if (SameJusticeClothing(current, uniform[index]))
                    restored &= WriteJusticeClothingIfChanged(player, original);
            }
            catch (Exception ex) { restored = false; LogException("Justice.Tenue.Restitution", ex); }
        }
        if (restored && !provisional)
        {
            _justiceCustodyAppearance = null;
            if (!JusticeIsCustodyActive && !_justiceCustodyPlayerStateStored &&
                !_justiceDeferredInventoryRestore && _justiceWeaponSnapshot == null)
            {
                _justiceCustodyPlayerHandle = 0;
                _justiceCustodyPlayerModelHash = 0;
                _justiceCustodyPlayerSlot = -1;
            }
            JusticeMarkStateDirty();
        }
        return restored;
    }

    private void RetryJusticeCustodyAppearanceRestore(Ped player, int now)
    {
        if (_justiceCustodyAppearance == null || !_justiceCustodyAppearance.RestorePending || JusticeIsCustodyActive ||
            !JusticeCustodyHasReached(now, _justiceNextCustodyAppearanceAt)) return;
        _justiceNextCustodyAppearanceAt = JusticeCustodyFutureTime(now, JusticeCustodyPersonalEffectsCheckMs);
        RestoreJusticeCustodyAppearance(player);
    }

    private static void WriteJusticeAppearanceXml(XmlWriter writer, JusticeAppearancePersistenceSnapshot snapshot)
    {
        if (snapshot == null) return;
        writer.WriteStartElement("AppearanceSnapshot");
        writer.WriteAttributeString("episodeId", snapshot.EpisodeId);
        WriteJusticePersistenceAttribute(writer, "playerSlot", snapshot.PlayerSlot);
        WriteJusticePersistenceAttribute(writer, "modelHash", snapshot.ModelHash);
        WriteJusticePersistenceAttribute(writer, "restorePending", snapshot.RestorePending);
        foreach (JusticeClothingItem item in snapshot.Items)
        {
            writer.WriteStartElement(item.Prop ? "Prop" : "Component");
            WriteJusticePersistenceAttribute(writer, "slot", item.Slot);
            WriteJusticePersistenceAttribute(writer, "drawable", item.Drawable);
            WriteJusticePersistenceAttribute(writer, "texture", item.Texture);
            WriteJusticePersistenceAttribute(writer, "palette", item.Palette);
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static bool TryReadJusticeAppearanceXml(XmlElement custody, out JusticeAppearancePersistenceSnapshot snapshot)
    {
        snapshot = null;
        XmlNodeList nodes = custody.SelectNodes("AppearanceSnapshot");
        if (nodes.Count == 0) return true;
        if (nodes.Count != 1) return false;
        XmlElement element = (XmlElement)nodes[0];
        string episode = element.GetAttribute("episodeId");
        int slot, model; bool pending;
        if (episode.Length == 0 || episode.Length > 128 ||
            !element.HasAttribute("playerSlot") || !element.HasAttribute("modelHash") ||
            !TryReadJusticeIntStrict(element, "playerSlot", -1, 0, 2, out slot) ||
            !TryReadJusticeIntStrict(element, "modelHash", 0, int.MinValue, int.MaxValue, out model) || model == 0 ||
            !TryReadJusticeBoolStrict(element, "restorePending", false, out pending)) return false;
        JusticeClothingItem[] expected = GetJusticeCustodyUniform(slot);
        List<JusticeClothingItem> items = new List<JusticeClothingItem>();
        foreach (XmlNode node in element.ChildNodes)
        {
            XmlElement entry = node as XmlElement;
            if (entry == null) continue;
            if (items.Count >= expected.Length) return false;
            JusticeClothingItem key = expected[items.Count];
            int component, drawable, texture, palette;
            if (entry.Name != (key.Prop ? "Prop" : "Component") ||
                !entry.HasAttribute("slot") || !entry.HasAttribute("drawable") || !entry.HasAttribute("texture") || !entry.HasAttribute("palette") ||
                !TryReadJusticeIntStrict(entry, "slot", -1, key.Slot, key.Slot, out component) ||
                !TryReadJusticeIntStrict(entry, "drawable", -2, key.Prop ? -1 : 0, 4095, out drawable) ||
                !TryReadJusticeIntStrict(entry, "texture", -1, 0, 4095, out texture) ||
                !TryReadJusticeIntStrict(entry, "palette", -1, 0, key.Prop ? 0 : 3, out palette) ||
                (drawable == -1 && texture != 0)) return false;
            items.Add(new JusticeClothingItem(key.Prop, component, drawable, texture, palette));
        }
        if (items.Count != expected.Length) return false;
        snapshot = new JusticeAppearancePersistenceSnapshot(episode, slot, model, items, pending);
        return true;
    }
}
