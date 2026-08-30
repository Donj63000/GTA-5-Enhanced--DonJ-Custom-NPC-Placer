using System;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public sealed class JusticeRecoveryHashContractTests
{
    [TestMethod]
    public void NormalV2Read_MissingRecoveryHashRejectsPrimaryAndKeepsBackupReadable()
    {
        JusticeXmlPersistenceCodec codec = new JusticeXmlPersistenceCodec();
        byte[] primary = codec.Serialize(CreateSnapshot(42L, "primary"));
        byte[] backup = codec.Serialize(CreateSnapshot(41L, "backup"));

        JusticePersistenceSnapshot decoded;
        string error;
        Assert.IsTrue(codec.TryDeserialize(primary, out decoded, out error), error);
        Assert.AreEqual(42L, decoded.Revision);

        XmlDocument xml = LoadXml(primary);
        string recoveryHash = xml.DocumentElement.GetAttribute("recoverySha256");
        Assert.AreEqual(64, recoveryHash.Length);
        byte[] primaryWithoutRecoveryProof = RemoveRootAttribute(
            primary,
            "recoverySha256",
            recoveryHash);

        Assert.IsFalse(
            codec.TryDeserialize(
                primaryWithoutRecoveryProof,
                out decoded,
                out error),
            "Un document v2 normal ne doit jamais être accepté sans sa preuve de récupération.");
        StringAssert.Contains(error, "récupération");
        Assert.IsNull(decoded);

        Assert.IsTrue(
            codec.TryDeserialize(backup, out decoded, out error),
            "Le rejet du primaire sans preuve doit laisser le backup v2 valide utilisable : " + error);
        Assert.AreEqual(41L, decoded.Revision);
        Assert.AreEqual(
            "backup",
            JusticeXmlPersistenceCodec.GetFieldValue(
                decoded.GlobalFields,
                "testAuthority",
                string.Empty));
    }

    private static JusticePersistenceSnapshot CreateSnapshot(
        long revision,
        string authority)
    {
        List<JusticePersistenceProfileSnapshot> profiles =
            new List<JusticePersistenceProfileSnapshot>();
        for (int slot = 0; slot < 3; slot++)
        {
            profiles.Add(new JusticePersistenceProfileSnapshot(
                slot,
                revision + slot,
                "slot:" + slot,
                new[]
                {
                    new JusticePersistenceField("Case", "<Case enabled=\"false\" />"),
                    new JusticePersistenceField("Record", "<Record />"),
                    new JusticePersistenceField("Custody", "<Custody />")
                }));
        }

        return new JusticePersistenceSnapshot(
            revision,
            JusticeXmlPersistenceCodec.SchemaMajor,
            DateTime.UtcNow.Ticks,
            1,
            new[]
            {
                new JusticePersistenceField("activePlayerSlot", "1"),
                new JusticePersistenceField("testAuthority", authority)
            },
            profiles);
    }

    private static XmlDocument LoadXml(byte[] document)
    {
        XmlDocument xml = new XmlDocument { XmlResolver = null };
        xml.LoadXml(Encoding.UTF8.GetString(document));
        Assert.IsNotNull(xml.DocumentElement);
        return xml;
    }

    private static byte[] RemoveRootAttribute(
        byte[] document,
        string name,
        string value)
    {
        string xml = Encoding.UTF8.GetString(document);
        string attribute = " " + name + "=\"" + value + "\"";
        StringAssert.Contains(xml, attribute);
        return Encoding.UTF8.GetBytes(xml.Replace(attribute, string.Empty));
    }
}
