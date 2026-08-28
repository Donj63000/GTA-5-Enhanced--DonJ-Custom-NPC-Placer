using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GTA.Native;

namespace GTA
{
    public sealed class StubNativeInvocation
    {
        public StubNativeInvocation(ulong hash, object[] arguments)
        {
            Hash = hash;
            Arguments = arguments ?? new object[0];
        }

        public ulong Hash { get; private set; }
        public object[] Arguments { get; private set; }
    }

    public static class StubRuntime
    {
        private static readonly object SyncRoot = new object();
        private static readonly List<StubNativeInvocation> RecordedNativeCalls =
            new List<StubNativeInvocation>();

        public static Func<ulong, object[], object> NativeCallHandler { get; set; }
        public static Func<Entity, Entity, bool> DamageHandler { get; set; }
        public static Func<Ped, Ped, bool> CombatHandler { get; set; }
        public static Func<Ped, Entity> KillerHandler { get; set; }
        public static Func<Player, Entity> TargetedEntityHandler { get; set; }
        public static Ped[] NearbyPeds { get; set; } = new Ped[0];
        public static Vehicle[] NearbyVehicles { get; set; } = new Vehicle[0];
        public static Vehicle[] AllVehicles { get; set; } = new Vehicle[0];

        public static IList<StubNativeInvocation> NativeCalls
        {
            get
            {
                lock (SyncRoot)
                {
                    return RecordedNativeCalls.ToArray();
                }
            }
        }

        public static void Reset()
        {
            lock (SyncRoot)
            {
                RecordedNativeCalls.Clear();
            }

            NativeCallHandler = null;
            DamageHandler = null;
            CombatHandler = null;
            KillerHandler = null;
            TargetedEntityHandler = null;
            NearbyPeds = new Ped[0];
            NearbyVehicles = new Vehicle[0];
            AllVehicles = new Vehicle[0];
            Game.GameTime = 0;
            Game.LastFrameTime = 0.016f;
            Game.ScreenResolution = new Size(1280, 720);
            Game.IsLoading = false;
            Game.IsPaused = false;
            Game.MissionFlag = false;
            Game.Player.Character = new Ped();
            Game.Player.WantedLevel = 0;
            Game.Player.Money = 0;
            Game.Player.IsDead = false;
            Game.Player.CanControlCharacter = true;
        }

        internal static object InvokeNative(ulong hash, object[] arguments)
        {
            object[] safeArguments = arguments ?? new object[0];
            lock (SyncRoot)
            {
                RecordedNativeCalls.Add(new StubNativeInvocation(hash, safeArguments));
            }

            Func<ulong, object[], object> handler = NativeCallHandler;
            return handler == null ? null : handler(hash, safeArguments);
        }
    }

    public class Script
    {
        public int Interval { get; set; }
        public event EventHandler Tick;
        public event KeyEventHandler KeyDown;
        public event KeyEventHandler KeyUp;
        public event EventHandler Aborted;

        protected void RaiseTick() => Tick?.Invoke(this, EventArgs.Empty);
        protected void RaiseKeyDown(KeyEventArgs e) => KeyDown?.Invoke(this, e);
        protected void RaiseKeyUp(KeyEventArgs e) => KeyUp?.Invoke(this, e);
        protected void RaiseAborted() => Aborted?.Invoke(this, EventArgs.Empty);
        public static void Wait(int ms) { }
    }

    public unsafe class Entity
    {
        public int Handle { get; set; } = 1;
        public int* MemoryAddress { get; set; }
        public Math.Vector3 Position { get; set; }
        public Math.Vector3 Rotation { get; set; }
        public Math.Vector3 ForwardVector { get; set; } = new Math.Vector3(0.0f, 1.0f, 0.0f);
        public float Heading { get; set; }
        public float Speed { get; set; }
        public bool IsDead { get; set; }
        public bool IsPersistent { get; set; }
        public bool FreezePosition { get; set; }
        public bool IsInvincible { get; set; }
        public int Alpha { get; set; } = 255;

        public static bool Exists(Entity entity) => entity != null && entity.Handle != 0;
        public virtual bool Exists() => Exists(this);
        public virtual void Delete() => Handle = 0;
        public virtual void MarkAsNoLongerNeeded() { }
        public virtual Blip AddBlip() => new Blip();
        public bool IsTouching(Entity entity) => false;
        public bool HasBeenDamagedBy(Entity entity)
        {
            Func<Entity, Entity, bool> handler = StubRuntime.DamageHandler;
            return handler != null && handler(this, entity);
        }
        public void ClearLastWeaponDamage() { }
        public void SetNoCollision(Entity entity, bool toggle) { }
    }

    public class Ped : Entity
    {
        public int Health { get; set; } = 100;
        public int MaxHealth { get; set; } = 100;
        public int Armor { get; set; }
        public int Accuracy { get; set; }
        public int ShootRate { get; set; }
        public bool AlwaysKeepTask { get; set; }
        public bool BlockPermanentEvents { get; set; }
        public bool CanBeTargetted { get; set; } = true;
        public bool CanRagdoll { get; set; } = true;
        public bool CanSwitchWeapons { get; set; } = true;
        public bool IsEnemy { get; set; }
        public bool IsHuman { get; set; } = true;
        public bool IsPlayer { get; set; }
        public bool IsInCombat { get; set; }
        public bool IsInMeleeCombat { get; set; }
        public bool IsShooting { get; set; }
        public bool IsBeingStunned { get; set; }
        public bool IsJacking { get; set; }
        public bool IsBeingJacked { get; set; }
        public bool IsCuffed { get; set; }
        public Model Model { get; set; }
        public Vehicle CurrentVehicle { get; set; }
        public Vehicle LastVehicle { get; set; }
        public Vehicle VehicleTryingToEnter { get; set; }
        public VehicleSeat SeatIndex { get; set; } = VehicleSeat.Driver;
        public WeaponCollection Weapons { get; } = new WeaponCollection();
        public TaskInvoker Tasks { get; } = new TaskInvoker();

        public bool IsInVehicle() => CurrentVehicle != null;
        public bool IsInVehicle(Vehicle vehicle) => CurrentVehicle != null && vehicle != null && CurrentVehicle.Handle == vehicle.Handle;
        public bool IsInCombatAgainst(Ped ped)
        {
            Func<Ped, Ped, bool> handler = StubRuntime.CombatHandler;
            return handler != null && handler(this, ped);
        }
        public Ped GetJackTarget() => null;
        public Ped GetJacker() => null;
        public Entity GetKiller()
        {
            Func<Ped, Entity> handler = StubRuntime.KillerHandler;
            return handler == null ? null : handler(this);
        }
        public Relationship GetRelationshipWithPed(Ped ped) => Relationship.Neutral;
    }

    public class Vehicle : Entity
    {
        public float BodyHealth { get; set; } = 1000.0f;
        public float EngineHealth { get; set; } = 1000.0f;
        public float PetrolTankHealth { get; set; } = 1000.0f;
        public Ped Driver { get; set; }
        public bool IsDriveable { get; set; } = true;

        public void Repair() { }
        public bool IsSeatFree(VehicleSeat seat) => true;
        public Ped GetPedOnSeat(VehicleSeat seat) => null;
    }

    public class Prop : Entity
    {
    }

    public class Blip
    {
        public float Scale { get; set; }
        public bool IsShortRange { get; set; }
        public BlipSprite Sprite { get; set; }
        public BlipColor Color { get; set; }
        public bool IsFriendly { get; set; }
        public string Name { get; set; }
        public bool IsFlashing { get; set; }

        public bool Exists() => true;
        public void Remove() { }
    }

    public class Camera
    {
        public Math.Vector3 Position { get; set; }
        public Math.Vector3 Rotation { get; set; }
        public Math.Vector3 Direction { get; set; } = new Math.Vector3(0.0f, 1.0f, 0.0f);
        public float FarClip { get; set; }

        public static bool Exists(Camera camera) => camera != null;
        public void Destroy() { }
    }

    public struct Model
    {
        private readonly int _hash;
        private readonly string _name;

        public Model(int hash)
        {
            _hash = hash;
            _name = null;
        }

        public Model(string name)
        {
            _hash = Game.GenerateHash(name ?? string.Empty);
            _name = name;
        }

        public int Hash => _hash;
        public bool IsLoaded => true;
        public bool IsValid => true;
        public bool IsInCdImage => true;
        public bool IsPed => true;
        public bool IsVehicle => true;

        public bool Request(int timeout) => true;
        public void MarkAsNoLongerNeeded() { }
        public override string ToString() => _name ?? _hash.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    public sealed class WeaponCollection
    {
        private readonly HashSet<int> _weapons = new HashSet<int>();

        public int RemoveAllCount { get; private set; }
        public WeaponHash SelectedWeapon { get; private set; } = WeaponHash.Unarmed;

        public void RemoveAll()
        {
            RemoveAllCount++;
            _weapons.Clear();
            SelectedWeapon = WeaponHash.Unarmed;
        }

        public void Give(WeaponHash weapon, int ammo, bool equipNow, bool isAmmoLoaded)
        {
            _weapons.Add((int)weapon);
            if (equipNow)
            {
                SelectedWeapon = weapon;
            }
        }

        public void Select(WeaponHash weapon)
        {
            SelectedWeapon = weapon;
        }

        public void Select(WeaponHash weapon, bool equipNow)
        {
            if (equipNow)
            {
                SelectedWeapon = weapon;
            }
        }

        public bool HasWeapon(WeaponHash weapon) => _weapons.Contains((int)weapon);
    }

    public sealed class TaskInvoker
    {
        public void ClearAll() { }
        public void ClearAllImmediately() { }
        public void StandStill(int duration) { }
        public void FightAgainst(Ped target) { }
        public void FightAgainst(Ped target, int duration) { }
        public void GoTo(Entity target) { }
        public void GoTo(Math.Vector3 position) { }
        public void CruiseWithVehicle(Vehicle vehicle, float speed, int drivingStyle) { }
        public void DriveTo(Vehicle vehicle, Math.Vector3 position, float radius, float speed, int drivingStyle) { }
        public void WanderAround() { }
    }

    public sealed class Player
    {
        public Ped Character { get; set; } = new Ped();
        public int Handle { get; set; } = 1;
        public int WantedLevel { get; set; }
        public int Money { get; set; }
        public bool IsDead { get; set; }
        public bool CanControlCharacter { get; set; } = true;
        public bool IsAiming { get; set; }
        public bool IsTargettingAnything { get; set; }
        public Entity GetTargetedEntity()
        {
            Func<Player, Entity> handler = StubRuntime.TargetedEntityHandler;
            return handler == null ? null : handler(this);
        }
    }

    public static class Game
    {
        public static int GameTime { get; set; }
        public static float LastFrameTime { get; set; } = 0.016f;
        public static Size ScreenResolution { get; set; } = new Size(1280, 720);
        public static Player Player { get; } = new Player();
        public static bool IsLoading { get; set; }
        public static bool IsPaused { get; set; }
        public static bool MissionFlag { get; set; }

        public static bool IsKeyPressed(Keys key) => false;
        public static void DisableAllControlsThisFrame(int index) { }
        public static void DisableControlThisFrame(int index, Control control) { }
        public static float GetDisabledControlNormal(int index, Control control) => 0.0f;
        public static string GetUserInput(string defaultText, int maxLength) => defaultText;
        public static int GenerateHash(string value) => string.IsNullOrEmpty(value) ? 0 : StringComparer.OrdinalIgnoreCase.GetHashCode(value);
    }

    public static class World
    {
        public static Camera RenderingCamera { get; set; }

        public static int AddRelationshipGroup(string name) => Game.GenerateHash(name);
        public static void RemoveRelationshipGroup(int group) { }
        public static void SetRelationshipBetweenGroups(Relationship relationship, int groupA, int groupB) { }
        public static Ped CreatePed(Model model, Math.Vector3 position, float heading) => new Ped { Position = position, Heading = heading, Model = model };
        public static Vehicle CreateVehicle(Model model, Math.Vector3 position, float heading) => new Vehicle { Position = position, Heading = heading };
        public static Prop CreateProp(Model model, Math.Vector3 position, bool dynamic, bool placeOnGround) => new Prop { Position = position };
        public static Camera CreateCamera(Math.Vector3 position, Math.Vector3 rotation, float fov) => new Camera { Position = position, Rotation = rotation };
        public static Vehicle[] GetAllVehicles() => StubRuntime.AllVehicles ?? new Vehicle[0];
        public static Vehicle[] GetNearbyVehicles(Ped center, float radius) => StubRuntime.NearbyVehicles ?? new Vehicle[0];
        public static Ped[] GetNearbyPeds(Ped center, float radius) => StubRuntime.NearbyPeds ?? new Ped[0];
        public static Math.Vector3 GetSafeCoordForPed(Math.Vector3 position, bool sidewalk, int flags) => position;
        public static float GetGroundHeight(Math.Vector3 position) => position.Z;
        public static RaycastResult Raycast(Math.Vector3 source, Math.Vector3 target, IntersectOptions options, Entity ignoreEntity) => new RaycastResult();
        public static RaycastResult Raycast(Math.Vector3 source, Math.Vector3 direction, float maxDistance, IntersectOptions options, Entity ignoreEntity) => new RaycastResult();
        public static void DrawMarker(MarkerType type, Math.Vector3 position, Math.Vector3 direction, Math.Vector3 rotation, Math.Vector3 scale, Color color) { }
    }

    public sealed class RaycastResult
    {
        public bool DitHitAnything { get; set; }
        public Math.Vector3 HitCoords { get; set; }
        public Math.Vector3 SurfaceNormal { get; set; } = new Math.Vector3(0.0f, 0.0f, 1.0f);
    }

    public sealed class UIRectangle
    {
        public UIRectangle(Point position, Size size, Color color)
        {
            Position = position;
            Size = size;
            Color = color;
        }

        public Point Position { get; set; }
        public Size Size { get; set; }
        public Color Color { get; set; }
        public bool Enabled { get; set; } = true;

        public void Draw() { }
    }

    public sealed class UIText
    {
        public UIText(string caption, Point position, float scale, Color color, Font font, bool centered, bool shadow, bool outline)
        {
            Caption = caption;
            Position = position;
            Scale = scale;
            Color = color;
            Font = font;
            Centered = centered;
            Shadow = shadow;
            Outline = outline;
        }

        public string Caption { get; set; }
        public Point Position { get; set; }
        public float Scale { get; set; }
        public Color Color { get; set; }
        public Font Font { get; set; }
        public bool Centered { get; set; }
        public bool Shadow { get; set; }
        public bool Outline { get; set; }
        public bool Enabled { get; set; } = true;

        public void Draw() { }
    }

    public enum Control
    {
        Phone = 27,
        Attack = 24,
        Aim = 25,
        SelectWeapon = 37,
        Reload = 45,
        LookLeftRight = 1,
        LookUpDown = 2,
        WeaponWheelLeftRight = 12,
        WeaponWheelUpDown = 13
    }

    public enum Font
    {
        ChaletLondon = 0
    }

    public enum Relationship
    {
        Companion = 0,
        Neutral = 3,
        Dislike = 4,
        Hate = 5
    }

    public enum BlipSprite
    {
        Enemy2 = 303
    }

    public enum BlipColor
    {
        Red = 1,
        Green = 2,
        Blue = 3,
        Yellow = 5
    }

    [Flags]
    public enum IntersectOptions
    {
        Map = 1,
        Objects = 16,
        Vegetation = 256
    }

    public enum MarkerType
    {
        VerticalCylinder = 1,
        DebugSphere = 28
    }

    public enum VehicleSeat
    {
        Driver = -1,
        Passenger = 0,
        LeftRear = 1,
        RightRear = 2
    }

}

namespace GTA.Math
{
    public struct Vector3
    {
        public float X;
        public float Y;
        public float Z;

        public Vector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static Vector3 Zero => new Vector3(0.0f, 0.0f, 0.0f);
        public float Length() => (float)System.Math.Sqrt((X * X) + (Y * Y) + (Z * Z));
        public float DistanceTo(Vector3 other) => (this - other).Length();

        public static Vector3 operator +(Vector3 left, Vector3 right) => new Vector3(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        public static Vector3 operator -(Vector3 left, Vector3 right) => new Vector3(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        public static Vector3 operator -(Vector3 vector) => new Vector3(-vector.X, -vector.Y, -vector.Z);
        public static Vector3 operator *(Vector3 vector, float scale) => new Vector3(vector.X * scale, vector.Y * scale, vector.Z * scale);
        public static Vector3 operator *(float scale, Vector3 vector) => vector * scale;
        public static Vector3 operator /(Vector3 vector, float scale) => new Vector3(vector.X / scale, vector.Y / scale, vector.Z / scale);
    }
}

namespace GTA.Native
{
    public sealed class InputArgument
    {
        public InputArgument(ulong value)
            : this((object)value)
        {
        }

        private InputArgument(object value)
        {
            Value = value;
        }

        internal object Value { get; private set; }

        public static implicit operator InputArgument(int value) => new InputArgument(value);
        public static implicit operator InputArgument(bool value) => new InputArgument(value);
    }

    public enum WeaponHash
    {
        Unarmed = unchecked((int)0xA2719263),
        Knife = unchecked((int)0x99B507EA),
        Pistol = unchecked((int)0x1B06D571),
        MicroSMG = unchecked((int)0x13532244),
        SMG = unchecked((int)0x2BE6766B),
        MachinePistol = unchecked((int)0xDB1AA450),
        CarbineRifle = unchecked((int)0x83BF0278),
        ServiceCarbine = unchecked((int)0xD1D5F52B)
    }

    public enum VehicleHash
    {
        Adder = unchecked((int)0xB779A091),
        Baller6 = unchecked((int)0x27B4E6B0)
    }

    public enum PedHash
    {
        Swat01SMY = unchecked((int)0x8D8F1B10),
        Cop01SMY = unchecked((int)0x5E3DA4A4),
        Sheriff01SMY = unchecked((int)0xB144F9B9),
        Marine01SMY = unchecked((int)0xF2DAA2ED),
        BallaEast01GMY = unchecked((int)0xF42EE883),
        Business01AMM = unchecked((int)0x7E6A64B7),
        Business01AFY = unchecked((int)0x2799EFD8),
        Michael = unchecked((int)0x0D7114C9)
    }

    public enum WeaponComponentHash
    {
        AtPiSupp = unchecked((int)0xC304849A),
        AtArSupp = unchecked((int)0x837445AA),
        AtPiFlsh = unchecked((int)0x359B7AAE),
        AtArFlsh = unchecked((int)0x7BC4CDDC),
        AtArAfGrip = unchecked((int)0xC164F53),
        AtArAfGrip02 = unchecked((int)0x9D65907A),
        AtScopeSmall = unchecked((int)0xAA2C45B4),
        AtScopeMedium = unchecked((int)0xA0D89C42),
        AtScopeLarge = unchecked((int)0xD2443DDC),
        AtMuzzle1 = unchecked((int)0xB99402D4),
        AtMuzzle2 = unchecked((int)0xC867A07B),
        AtMuzzle3 = unchecked((int)0xDE11CBCF),
        AtMuzzle4 = unchecked((int)0xEC9068CC),
        AtMuzzle5 = unchecked((int)0x2E7957A),
        AtMuzzle6 = unchecked((int)0x347EF8AC),
        AtMuzzle7 = unchecked((int)0x4DB62ABE),
        AtArBarrel2 = unchecked((int)0xE73653A9),
        Clip01 = 0,
        Clip02 = 1
    }

    public enum Hash : ulong
    {
        CLEAR_ENTITY_LAST_DAMAGE_ENTITY = 0xA72CD9CA74A5ECBA,
        CLEAR_PLAYER_WANTED_LEVEL = 0xB302540597885499,
        CLEAR_PED_TASKS = 0xE1EF3C1216AFF2CD,
        DOES_ENTITY_EXIST = 0x7239B21A38F536BA,
        DOES_WEAPON_TAKE_WEAPON_COMPONENT = 0x5CEE3DF569CECAB0,
        DO_SCREEN_FADE_IN = 0xD4E8E24955024033,
        DO_SCREEN_FADE_OUT = 0x891B5B39AC6302AF,
        FREEZE_ENTITY_POSITION = 0x428CA6DBD1094446,
        GET_ENTITY_MODEL = 0x9F47B058362C84B5,
        GET_AMMO_IN_CLIP = 0x2E1202248937775C,
        GET_AMMO_IN_PED_WEAPON = 0x015A522136D7F951,
        GET_GAMEPLAY_CAM_COORD = 0x14D6F5678D8F1B37,
        GET_GAMEPLAY_CAM_ROT = 0x837765A25378F0BB,
        GET_SAFE_ZONE_SIZE = 0xBAF107B6BB2C97F0,
        GET_NTH_CLOSEST_VEHICLE_NODE = 0xE50E52416CCF948B,
        GET_PED_IN_VEHICLE_SEAT = 0xBB40DD2270B65366,
        GET_PED_LAST_WEAPON_IMPACT_COORD = 0x6C4D0409BA1A2BC2,
        GET_PED_RELATIONSHIP_GROUP_HASH = 0x7DBDD04862D95F04,
        GET_PED_WEAPON_TINT_INDEX = 0x2B9EEDC07BD06B9F,
        GET_SELECTED_PED_WEAPON = 0x0A6DB4965674D243,
        GET_TIME_SINCE_LAST_ARREST = 0x5063F92F07C2A316,
        GET_TIME_SINCE_LAST_DEATH = 0xC7034807558DDFCA,
        GET_TIME_SINCE_PLAYER_HIT_PED = 0xE36A25322DC35F42,
        GET_TIME_SINCE_PLAYER_HIT_VEHICLE = 0x5D35ECF3A81A0EE0,
        GET_VEHICLE_MAX_NUMBER_OF_PASSENGERS = 0xA7C4F2C6E744A550,
        GET_WEAPON_TINT_COUNT = 0x5DCF6C5CAB2E9BF7,
        GIVE_WEAPON_COMPONENT_TO_PED = 0xD966D51AA5B28BB9,
        HAS_ENTITY_BEEN_DAMAGED_BY_ENTITY = 0xC86D67D52A707CF8,
        HAS_ENTITY_CLEAR_LOS_TO_ENTITY = 0xFCDFF7B72D23A1AC,
        HAS_ENTITY_CLEAR_LOS_TO_ENTITY_IN_FRONT = 0x0267D00AF114F17A,
        HAS_PED_GOT_WEAPON = 0x8DECB02F88F428BC,
        HAS_PED_GOT_WEAPON_COMPONENT = 0xC593212475FAE340,
        HAS_PLAYER_BEEN_SPOTTED_IN_STOLEN_VEHICLE = 0xD705740BB0A1CF4C,
        HIDE_HUD_AND_RADAR_THIS_FRAME = 0x719FF505F097FD20,
        IS_BULLET_IN_AREA = 0x3F2023999AD51C1F,
        IS_DISABLED_CONTROL_JUST_PRESSED = 0x91AEF906BCA88877,
        IS_ENTITY_A_VEHICLE = 0x6AC7003FA6E5575E,
        IS_ENTITY_ON_SCREEN = 0xE659E47AF827484B,
        IS_ENTITY_TOUCHING_ENTITY = 0x17FFC1B2BA35A494,
        IS_PED_IN_COMBAT = 0x4859F1FC66A6278E,
        IS_PED_IN_MELEE_COMBAT = 0x4E209B2C1EAD5159,
        IS_PED_HUMAN = 0xB980061DA992779D,
        IS_PED_JACKING = 0x4AE4FF911DFB61DA,
        IS_PED_SHOOTING = 0x34616828CD07F1A1,
        IS_PLAYER_BEING_ARRESTED = 0x388A47C51ABDAC8E,
        IS_PLAYER_FREE_AIMING_AT_ENTITY = 0x3C06B5C839B38F7B,
        IS_PLAYER_TARGETTING_ENTITY = 0x7912F7FC4F6264B6,
        IS_VEHICLE_DRIVEABLE = 0x4C241E39B23DF959,
        IS_VEHICLE_SEAT_FREE = 0x22AC59A870E6A669,
        REQUEST_COLLISION_AT_COORD = 0x07503F7948F491A7,
        REQUEST_IPL = 0x41B4893843BBDB74,
        REMOVE_ALL_PED_WEAPONS = 0xF25DF915FA38C5F3,
        RESET_ENTITY_ALPHA = 0x9B1E824FFBB7027A,
        SET_DRIVE_TASK_CRUISE_SPEED = 0x5C9B84BD7D31D908,
        SET_DRIVE_TASK_DRIVING_STYLE = 0xDACE1BE37D88AF67,
        SET_DRIVER_ABILITY = 0xB195FFA8042FC5C3,
        SET_DRIVER_AGGRESSIVENESS = 0xA731F608CA104E3C,
        SET_ENTITY_ALPHA = 0x44A0870B7E92D7C0,
        SET_ENTITY_AS_MISSION_ENTITY = 0xAD738C3085FE7E11,
        SET_ENTITY_COLLISION = 0x1A9205C1B9EE827F,
        SET_ENTITY_INVINCIBLE = 0x3882114BDE571AD4,
        SET_ENTITY_VELOCITY = 0x1C99BB7B6E96D16F,
        SET_ENTITY_VISIBLE = 0xEA1C610A04DB6BBB,
        SET_PED_ALERTNESS = 0xDBA71115ED9941A6,
        SET_PED_CAN_BE_DRAGGED_OUT = 0xC1670E958EEE24E5,
        SET_PED_CAN_BE_TARGETTED = 0x63F58F7C80513AAD,
        SET_PED_CAN_RAGDOLL = 0xB128377056A54E2A,
        SET_PED_COMBAT_ABILITY = 0xC7622C0D36B2FDA8,
        SET_PED_COMBAT_ATTRIBUTES = 0x9F7794730795E019,
        SET_PED_COMBAT_MOVEMENT = 0x4D9CA1009AFBD057,
        SET_PED_COMBAT_RANGE = 0x3C606747B23E497B,
        SET_PED_DROPS_WEAPONS_WHEN_DEAD = 0x476AE72C1D19D1A8,
        SET_PED_FIRING_PATTERN = 0x9AC577F5A12AD8A9,
        SET_PED_FLEE_ATTRIBUTES = 0x70A2D1137C8ED7C9,
        SET_PED_HEARING_RANGE = 0x33A8F7F7D5F7F33C,
        SET_PED_INTO_VEHICLE = 0xF75B0D629E1C063D,
        SET_PED_RELATIONSHIP_GROUP_HASH = 0xC80A74AC829DDD92,
        SET_PED_SEEING_RANGE = 0xF29CF591C4BF6CEE,
        SET_PED_STAY_IN_VEHICLE_WHEN_JACKED = 0xEDF4079F9D54C9A1,
        SET_PED_SUFFERS_CRITICAL_HITS = 0xEBD76F2359F190AC,
        SET_PED_WEAPON_TINT_INDEX = 0x50969B9B89ED5738,
        SET_AMMO_IN_CLIP = 0xDCD2A934D65CB497,
        SET_CURRENT_PED_WEAPON = 0xADF692B254977C0C,
        SET_VEHICLE_COLOURS = 0x4F1D4BE3A7F24601,
        SET_VEHICLE_DIRT_LEVEL = 0x79D3B596FE44EE8B,
        SET_VEHICLE_DOORS_LOCKED = 0xB664292EAECF7FA6,
        SET_VEHICLE_ENGINE_HEALTH = 0x45F6D8EEF34ABEF1,
        SET_VEHICLE_ENGINE_ON = 0x2497C4717C8B881E,
        SET_VEHICLE_EXTRA_COLOURS = 0x2036F561ADD12E33,
        SET_VEHICLE_FORWARD_SPEED = 0xAB54A438726D25D5,
        SET_VEHICLE_MOD = 0x6AF0636DDEDCB6DD,
        SET_VEHICLE_MOD_KIT = 0x1F2AA07F00B3217A,
        SET_VEHICLE_ON_GROUND_PROPERLY = 0x49733E92263139D1,
        SET_VEHICLE_PETROL_TANK_HEALTH = 0x70DB57649FA8D0D8,
        SET_VEHICLE_TYRES_CAN_BURST = 0xEB9DC3C7D8596C46,
        SET_VEHICLE_WINDOW_TINT = 0x57C51E6BAD752696,
        TASK_ACHIEVE_HEADING = 0x93B93A37987F1F3D,
        TASK_COMBAT_PED = 0xF166E48407BAC484,
        TASK_DRIVE_BY = 0x2F8AF0E82773A171,
        TASK_ENTER_VEHICLE = 0xC20E50AA46D09CA8,
        TASK_FOLLOW_NAV_MESH_TO_COORD = 0x15D3A79D4E44B913,
        TASK_FOLLOW_TO_OFFSET_OF_ENTITY = 0x304AE42E357B8C7E,
        TASK_GO_TO_ENTITY = 0x6A071245EB0D1882,
        TASK_LEAVE_VEHICLE = 0xD3DBCE61A490BE02,
        TASK_SHOOT_AT_ENTITY = 0x08DA95E8298AE772,
        TASK_STAND_STILL = 0x919BE13EED931959,
        TASK_START_SCENARIO_IN_PLACE = 0x142A02425FF02BD9,
        TASK_TURN_PED_TO_FACE_ENTITY = 0x5AD23D40115353AC,
        TASK_VEHICLE_DRIVE_TO_COORD_LONGRANGE = 0x158BB33F920D360C,
        TASK_VEHICLE_ESCORT = 0x0FA6E4B75F302400,
        TASK_WANDER_STANDARD = 0xBB9CE077274F6A1B,
        TOGGLE_VEHICLE_MOD = 0x2A1F4F37F95BAD08
    }

    public sealed class OutputArgument
    {
        private object _value;

        public void SetResult<T>(T value)
        {
            _value = value;
        }

        public T GetResult<T>()
        {
            if (_value == null)
            {
                return default(T);
            }

            if (_value is T)
            {
                return (T)_value;
            }

            Type targetType = typeof(T);
            if (targetType.IsEnum)
            {
                return (T)Enum.ToObject(targetType, _value);
            }

            return (T)Convert.ChangeType(_value, targetType);
        }
    }

    public static class Function
    {
        public static T Call<T>(Hash hash, params object[] arguments) =>
            ConvertResult<T>(GTA.StubRuntime.InvokeNative((ulong)hash, arguments));

        public static void Call(Hash hash, params object[] arguments)
        {
            GTA.StubRuntime.InvokeNative((ulong)hash, arguments);
        }

        public static T Call<T>(ulong hash, params object[] arguments) =>
            ConvertResult<T>(GTA.StubRuntime.InvokeNative(hash, arguments));

        public static void Call(ulong hash, params object[] arguments)
        {
            GTA.StubRuntime.InvokeNative(hash, arguments);
        }

        private static T ConvertResult<T>(object value)
        {
            if (value == null)
            {
                return default(T);
            }

            if (value is T)
            {
                return (T)value;
            }

            Type targetType = typeof(T);
            if (targetType.IsEnum)
            {
                return (T)Enum.ToObject(targetType, value);
            }

            return (T)Convert.ChangeType(value, targetType);
        }
    }
}
