// Multiplayer for Project Arena (native app/src/net.h, net.cpp, native commit 0eea0e7; rooms for four, the host
// handoff and idle rooms as native a7230f8, task B2 and B3), ported 1:1 so Unity and native headsets share rooms on the LAN and on the community relay:
// the same packets byte for byte (protocol version 10).
// Every arena is a ROOM run by its host headset (host-authoritative simulation):
//   tank      two player slots (a human, the practice bot in slot 1, or empty); FOUR CORNERS: four (bots in the
//             empty ones when BOT FILLS IN; the host moves people between corners: MOVE PLAYERS)
//   waiting   up to 4 people in the holding area outside the tank (a room for four: eight spots); they can JOIN QUEUE
//             (as many as the tank takes). When a match ends the players leave the tank and the queue goes in; a room
//             for four sends everyone back to the waiting area after every match, and starts on READY UP.
//   spectators any number, invisible to the players: in the lobby (overlord view) or near this arena
//   discovery : the host broadcasts BEACON on UDP 47777 every 0.5 s (room counts, score, clock)
//   members   : send INPUT (head / hands / triggers / avatar) every frame to the host; QUEUE to join / leave the queue
//   host      : steps Game with the two tank players' inputs; sends STATE (game + events) and ROOM (slots, waiting
//               players' poses and avatars, spectator positions) to every member and spectator
//   spectator : SPECTATE every 0.2 s to every host it hears, with where it is (lobby or near that arena)
// Online, the community relay (native server/projectarena_relay.py) carries the same packets between virtual
// addresses. Addresses are kept as the native code keeps them: the IPv4 address's bytes in memory order (s_addr), or
// the relay's virtual address. Platform independent (System.Net.Sockets); Log and AcquireMulticastLock are set by the
// game. Tested by Tests/EditMode/RoomTests.cs (the native test_rooms.cpp against a local relay).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ProjectArena.Sim;

namespace ProjectArena.Online
{
    public class LanGame
    {
        public uint ip, session;
        public string name = "";
        public bool advanced, full;
        // MATCH OPTIONS (0.3.8 hosts; older ones: the defaults): shields off = HOTDOG, knuckle guards -1 as the rules say,
        // basic match time (0 = free play) and advanced round time (0 = none), seconds
        public bool shields = true;
        public int knuckleMode = -1;
        public float matchTime = 180f, roundTime = 0f;
        public int score0, score1, spectators, waiting, queued, inTank;
        public float clock;
        public double seen;
        // protocol 9 (native 0.3.12): the EXPERIMENTAL room (expRoom 0 DIAMOND, 1 FOUR CORNERS, 2 FIREBALL BLITZ), the
        // players (2 or 4), the teams and each team's score; protocol 10 (0.3.13): the host it was handed over from
        public bool experimental;
        public int expRoom, players = 2, teamMode;
        public int[] teamScores = { 0, 0, 0, 0 };
        public uint continues;
        // the rooms this port can play: two-player Basic / Advanced, and every EXPERIMENTAL room (DIAMOND, CORNERS; FOUR
        // CORNERS and FIREBALL BLITZ behind Net.NewRoomsShown)
        public bool Playable => !experimental ? players == 2 : players == 2 || Net.NewRoomsShown;
        public LanGame Copy() { var c = (LanGame)MemberwiseClone(); c.teamScores = (int[])teamScores.Clone(); return c; }
    }

    // One arena's people, as the host reports them.
    public class RoomInfo
    {
        public const byte Waiting = 2;
        public class Member { public uint id; public byte role; public sbyte queuePos = -1; public byte spot; public PlayerInput @in = new PlayerInput(); }
        public class Watcher { public uint id; public byte where; public Vec3 pos; }   // where: 0 lobby (lobby space), 1 this arena
        public uint[] slot = { 0, 0, 0, 0 }; // member id in the tank; 0 = empty (or a bot, see botMask)
        public bool bot;                     // slot 1 is the practice bot (two players)
        public byte botMask;                 // the slots the bots play (bit p); two players: 2 when `bot`
        public bool IsBot(int p) => ((botMask >> p) & 1) != 0;
        public List<Member> members = new List<Member>();   // everyone in the room except the tank slots' poses (in STATE)
        public List<Watcher> watchers = new List<Watcher>();
        // player names (REPORT / MUTE A PLAYER): the host, the tank, the waiting players and the spectators at this
        // arena. Sent after the rest of ROOM, so builds without it still read the packet.
        public class Name { public uint id; public string name = ""; }
        public List<Name> names = new List<Name>();
        public string NameOf(uint id) { foreach (var n in names) if (n.id == id) return n.name; return null; }
        // online, hosts from 0.3.24 (MUTED PLAYERS, native 3fccaf9): each person's virtual address on the community server
        // (the relay's own numbers, 10.x: no one's internet address), after the names. A guest and a spectator hear only
        // the host directly, so without them they could ask the server for the host's code alone (review 2026-11-21)
        public struct Addr { public uint id, vip; }
        public List<Addr> addrs = new List<Addr>();
    }

    // The Crowd: fans following an online match on their phones (the crowd web app, through the relay). Cheers in the
    // last half second, hype 0..100 and fans waving a flag, for each end; at = when it arrived (Net's clock).
    public class CrowdInfo { public int fans; public int[] cheers = { 0, 0 }, hype = { 0, 0 }, waves = { 0, 0 }; public double at = -100; }

    // A live match a spectator is subscribed to: the host's snapshots applied to a local Game (not simulated).
    public class SpecMatch
    {
        public uint ip;
        public string name = "";
        public Game game = new Game();
        public RoomInfo room = new RoomInfo();
        public uint lastSeq, lastEventId, lastRoomSeq;
        public double lastSeen;            // its last room update or snapshot (it stays listed)
        public double stateSeen = -1e9;    // its last new snapshot: a game being played (native 0.3.16: a room that only
                                           // sends its room updates is WAITING, not a LIVE MATCH)
        public byte[] state = new byte[0];
        public bool fresh;
        public List<GameEvent> events = new List<GameEvent>();   // new events since they were last taken
        public CrowdInfo crowd = new CrowdInfo();                 // its fans (The Crowd), as its host passes them on
    }

    // G.711 mu-law: 16-bit PCM <-> 8-bit, for voice chat (native voice_codec.h; 16 kHz mono = 16 KB/s per talker).
    public static class VoiceCodec
    {
        public static byte Encode(short pcm)
        {
            const int BIAS = 0x84, CLIP = 32635;
            int s = pcm, sign = (s >> 8) & 0x80;
            if (sign != 0) s = -s;
            if (s > CLIP) s = CLIP;
            s += BIAS;
            int exp = 7;
            for (int mask = 0x4000; (s & mask) == 0 && exp > 0; mask >>= 1) --exp;
            int mant = (s >> (exp + 3)) & 0x0F;
            return (byte)~(sign | (exp << 4) | mant);
        }

        public static short Decode(byte u)
        {
            u = (byte)~u;
            int sign = u & 0x80, exp = (u >> 4) & 7, mant = u & 0x0F;
            int s = ((mant << 3) + 0x84) << exp;
            s -= 0x84;
            return (short)(sign != 0 ? -s : s);
        }
    }

    // little-endian packet writing and reading (the native Writer / Reader: raw struct bytes on a little-endian CPU)
    public class Writer
    {
        public readonly List<byte> b = new List<byte>(256);
        public void U8(byte v) => b.Add(v);
        public void I8(sbyte v) => b.Add((byte)v);
        public void Bool(bool v) => b.Add(v ? (byte)1 : (byte)0);
        public void U16(ushort v) { b.Add((byte)v); b.Add((byte)(v >> 8)); }
        public void I16(short v) => U16((ushort)v);
        public void U32(uint v) { b.Add((byte)v); b.Add((byte)(v >> 8)); b.Add((byte)(v >> 16)); b.Add((byte)(v >> 24)); }
        public void I32(int v) => U32((uint)v);
        public void F32(float v) => U32(BitConverter.ToUInt32(BitConverter.GetBytes(v), 0));
        public void F64(double v) => b.AddRange(BitConverter.GetBytes(v));
        public void V3(Vec3 v) { F32(v.x); F32(v.y); F32(v.z); }
        public void Q(Quat q) { F32(q.x); F32(q.y); F32(q.z); F32(q.w); }
        public void Pose(Pose p) { Q(p.q); V3(p.p); }
        public void Bytes(byte[] d, int off, int n) { for (int i = 0; i < n; ++i) b.Add(d[off + i]); }
        public void Avatar(AvatarDesc a)
        {
            U8(a.suit); U8(a.build); U8(a.hair); U8(a.hairColor); U8(a.headgear); U8(a.visor); U8(a.visorTint); U8(a.mask);
            U8(a.pattern); U8(a.primary); U8(a.secondary); U8(a.glow); U8(a.skin); U8(a.lower); U8(a.body); U8(a.gauntlet);
        }
        public byte[] ToArray() => b.ToArray();
    }

    public class Reader
    {
        public readonly byte[] d;
        public int p;
        public readonly int e;
        public bool ok = true;
        public Reader(byte[] d, int start, int end) { this.d = d; p = start; e = end; }
        public int Left => e - p;
        bool Need(int n) { if (p + n > e) { ok = false; return false; } return true; }
        public byte U8() { if (!Need(1)) return 0; return d[p++]; }
        public sbyte I8() => (sbyte)U8();
        public ushort U16() { if (!Need(2)) return 0; ushort v = (ushort)(d[p] | d[p + 1] << 8); p += 2; return v; }
        public short I16() => (short)U16();
        public uint U32() { if (!Need(4)) return 0; uint v = BitConverter.ToUInt32(d, p); p += 4; return v; }
        public int I32() => (int)U32();
        public float F32() { if (!Need(4)) return 0; float v = BitConverter.ToSingle(d, p); p += 4; return v; }
        public double F64() { if (!Need(8)) return 0; double v = BitConverter.ToDouble(d, p); p += 8; return v; }
        public Vec3 V3() { float x = F32(), y = F32(), z = F32(); return new Vec3(x, y, z); }
        public Quat Q() { float x = F32(), y = F32(), z = F32(), w = F32(); return new Quat(x, y, z, w); }
        public Pose Pose() { Quat q = Q(); Vec3 v = V3(); return new Pose(q, v); }
        public AvatarDesc Avatar()
        {
            var a = new AvatarDesc();
            a.suit = U8(); a.build = U8(); a.hair = U8(); a.hairColor = U8(); a.headgear = U8(); a.visor = U8(); a.visorTint = U8(); a.mask = U8();
            a.pattern = U8(); a.primary = U8(); a.secondary = U8(); a.glow = U8(); a.skin = U8(); a.lower = U8(); a.body = U8(); a.gauntlet = U8();
            return a;
        }
    }

    public class Net
    {
        public enum Mode { Offline, Searching, Host, Client, Spectate }
        public const int kMaxWaiting = 4, kMaxQueue = 4;   // (the queue: as many as the tank takes, 2 or 4)
        // the lobby lists rooms for four (FOUR CORNERS) as playable: on since its four players are drawn (task C3)
        public static bool NewRoomsShown = true;
        public const ushort kPort = 47777;
        public const uint kMagic = 0x43525053;   // 'SPRC'
        // 3: event log in STATE; 4: named beacons, LEAVE; 5: SpawnRise; 6: spectators; 7: avatars; 8: rooms (waiting
        // players, queue, rotation, spectator presence); 9 (native 0.3.12): four places, compact snapshots, the
        // EXPERIMENTAL rooms' rules in beacons and snapshots; 10 (0.3.13): FIREBALL BLITZ in STATE (and, appended to
        // packets older builds stop reading early, the shot clocks and the handed-over host)
        public const byte kVersion = 10;
        // The native version this template plays as (TASKS.md E1): told to the relay in HELLO and, from native 0.3.20, at
        // the end of JOIN, where FAIR MATCHES compares it with the version each rule-changing option came in (Tunables
        // kVer*). Only what it really plays: raise it once every option of that version is ported (0.3.20: FOUL LINE;
        // 0.3.21: the reach round the fist, the knuckles on the fist, the room options; 0.3.22: BOUNCES, the palm).
        // The builds take it as their version (Editor/Builds.cs)
        public const string GameVersion = "0.3.24";   // (native b54a757: 0.3.23 never shipped; 0.3.24 has it all, E44-E57, and FIREBALL BLITZ in teams online)
        const int kWireSlots = Teams.MaxPlayers;   // tank places on the wire
        // PING / PONG: the round trip between a host and each player (builds without them ignore both)
        const byte MSG_BEACON = 1, MSG_JOIN = 2, MSG_INPUT = 3, MSG_STATE = 4, MSG_LEAVE = 5, MSG_SPECTATE = 6, MSG_REJECT = 7,
            MSG_QUEUE = 8, MSG_ROOM = 9, MSG_VOICE = 10, MSG_CAMERAS = 11, MSG_PING = 12, MSG_PONG = 13, MSG_KICKVOTE = 14, MSG_CROWD = 15,
            MSG_HEIRS = 16, MSG_HANDOFF = 17, MSG_CLOSING = 18;
        // (16-18, native 0.3.13, HOST HANDOFF and IDLE ROOMS, host -> room. HEIRS: flags, the room's name, then who hosts
        //  next in order (id, address, name), then the people the host removed; HANDOFF: the new host's id and whether the
        //  old one stays; CLOSING: seconds left, 0 closed)
        const double kHostQuiet = 5.0;    // a host silent this long: the next one takes over
        const double kWakeKnock = 30.0;   // woken up: how long we keep asking a host that still announces its room (native 8f1fbdc)
        const double kHeirWait = 8.0;     // ... and this long for each heir to answer before the one after
        const double kResumeWait = 9.0;   // a room taken over waits this long for its players to come back
        // community relay (server/projectarena_relay.py)
        public const uint kRelayMagic = 0x4C52414F;   // 'OARL'
        public const ushort kRelayPort = 47780;
        const byte R_HELLO = 1, R_WELCOME = 2, R_SEND = 3, R_PING = 4, R_BYE = 5, R_DELIVER = 6, R_ERROR = 7, R_KEY = 8, R_TANK = 9,
            R_MATCH = 10, R_STATSREQ = 11, R_STATS = 12, R_CONFIRM = 13, R_DELETE = 14, R_MYSTATS = 15, R_LIVE = 16, R_CROWD = 17,
            R_TRAIN = 18, R_BOARDREQ = 19, R_BOARD = 20,   // (18-20: TRAINING's leaderboards, native 0.3.12)
            R_SOUNDVOTE = 21,                                // (21: SOUND STUDIO's votes, native 0.3.16)
            R_MUTECODE = 22;                                 // (22: MUTED PLAYERS' codes, native 0.3.23, TASKS.md E46)
        const double kRotateDelay = 6.0;   // match summary -> both players out, the queue in
        const double kMemberTimeout = 4.0;
        public const uint Broadcast = 0xFFFFFFFFu;

        public static Action<string> Log = _ => { };
        public static Func<bool> AcquireMulticastLock = () => false;   // Android: a Wi-Fi MulticastLock (the game sets it)

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static double MonoNow() => clock.Elapsed.TotalSeconds + 1.0;

        public static string IpStr(uint ip) => $"{ip & 255}.{(ip >> 8) & 255}.{(ip >> 16) & 255}.{ip >> 24}";
        static uint IpOf(IPAddress a) { byte[] b = a.GetAddressBytes(); return BitConverter.ToUInt32(b, 0); }
        static IPAddress AddrOf(uint ip) => new IPAddress(BitConverter.GetBytes(ip));

        // IDLE ROOMS: has this person moved (head 5 cm, a hand 10 cm) or changed a button since `ref`? (A headset left on
        // a desk still tracks: it just doesn't move.) Only what is tracked counts (native 0.3.16, owner 2026-11-06: a
        // host's headset left running unattended kept its room open all day; tracking that came and went made its head
        // and hands jump between where they were and a made-up place by the floor, and every jump counted as moving).
        // Tracking found again starts afresh from where it is, the jump not counted; without the head tracked only the
        // buttons count (nobody is wearing it). Returns what moved (Stir*), StirNone if nothing; the moved parts' `ref`
        // follow them.
        const byte StirNone = 0, StirHead = 1, StirHand = 2, StirButton = 3;
        static byte Stirred(Vec3[] @ref, ref byte refButtons, ref byte refTracked, PlayerInput @in)
        {
            byte b = @in.buttonA ? (byte)1 : (byte)0;
            for (int h = 0; h < 2; ++h)
                b |= (byte)(((@in.hand[h].trigger > 0.5f ? 2 : 0) << h) | ((@in.hand[h].squeeze > 0.5f ? 8 : 0) << h));
            Vec3[] cur = { @in.head.p, @in.hand[0].grip.p, @in.hand[1].grip.p };
            bool[] tracked = { @in.headTracked, @in.headTracked && @in.hand[0].tracked, @in.headTracked && @in.hand[1].tracked };
            byte why = b != refButtons ? StirButton : StirNone;
            byte t = 0;
            for (int k = 0; k < 3; ++k)
            {
                if (!tracked[k]) continue;
                t |= (byte)(1 << k);
                if (((refTracked >> k) & 1) == 0) { @ref[k] = cur[k]; continue; }   // (found again: from here)
                if (M.Length(cur[k] - @ref[k]) > (k != 0 ? 0.10f : 0.05f))
                {
                    if (why == StirNone) why = k != 0 ? StirHand : StirHead;
                    @ref[k] = cur[k];
                }
            }
            refTracked = t;
            refButtons = b;
            return why;
        }

        // ---------------------------------------------------------------- serialization
        static void WriteInput(Writer w, PlayerInput @in)
        {
            w.Pose(@in.head); w.Bool(@in.headTracked); w.Bool(@in.buttonA);
            for (int h = 0; h < 2; ++h)
            {
                w.Pose(@in.hand[h].grip); w.V3(@in.hand[h].angVel); w.Bool(@in.hand[h].tracked); w.F32(@in.hand[h].trigger); w.F32(@in.hand[h].squeeze);
            }
            w.Avatar(@in.avatar);
        }

        static void ReadInput(Reader r, PlayerInput @in)
        {
            @in.head = r.Pose(); @in.headTracked = r.U8() != 0; @in.buttonA = r.U8() != 0;
            for (int h = 0; h < 2; ++h)
            {
                @in.hand[h].grip = r.Pose(); @in.hand[h].angVel = r.V3(); @in.hand[h].tracked = r.U8() != 0;
                @in.hand[h].trigger = r.F32(); @in.hand[h].squeeze = r.F32();
            }
            var a = r.Avatar();
            Avatar.Sanitize(ref a);   // never trust indices from the network
            @in.avatar = a;
        }

        // the tank players in STATE (protocol 9): what the others draw and the host's game needs, quantised so four of
        // them fit the relay's 1200 bytes: rotations as four 16-bit numbers, triggers as bytes, no angular velocity
        static short Q16(float v) => (short)MathF.Round(Math.Clamp(v, -1f, 1f) * 32767f, MidpointRounding.AwayFromZero);
        static void PutQuat(Writer w, Quat q) { w.I16(Q16(q.x)); w.I16(Q16(q.y)); w.I16(Q16(q.z)); w.I16(Q16(q.w)); }
        static Quat GetQuat(Reader r)
        {
            float x = r.I16() / 32767f, y = r.I16() / 32767f, z = r.I16() / 32767f, wq = r.I16() / 32767f;
            float l = MathF.Sqrt(x * x + y * y + z * z + wq * wq);
            return l > 1e-3f && float.IsFinite(l) ? new Quat(x / l, y / l, z / l, wq / l) : Quat.Identity;
        }
        static byte ByteOf(float v) => (byte)MathF.Round(Math.Clamp(v, 0f, 1f) * 255f, MidpointRounding.AwayFromZero);

        static void WriteTankInput(Writer w, PlayerInput @in)
        {
            PutQuat(w, @in.head.q); w.V3(@in.head.p);
            w.U8((byte)((@in.headTracked ? 1 : 0) | (@in.buttonA ? 2 : 0) | (@in.hand[0].tracked ? 4 : 0) | (@in.hand[1].tracked ? 8 : 0)));
            for (int h = 0; h < 2; ++h)
            {
                PutQuat(w, @in.hand[h].grip.q); w.V3(@in.hand[h].grip.p); w.U8(ByteOf(@in.hand[h].trigger)); w.U8(ByteOf(@in.hand[h].squeeze));
            }
            w.Avatar(@in.avatar);
        }

        static void ReadTankInput(Reader r, PlayerInput @in)
        {
            @in.head = new Pose(GetQuat(r), r.V3());
            byte f = r.U8();
            @in.headTracked = (f & 1) != 0; @in.buttonA = (f & 2) != 0; @in.hand[0].tracked = (f & 4) != 0; @in.hand[1].tracked = (f & 8) != 0;
            for (int h = 0; h < 2; ++h)
            {
                @in.hand[h].grip = new Pose(GetQuat(r), r.V3());
                @in.hand[h].trigger = r.U8() / 255f; @in.hand[h].squeeze = r.U8() / 255f;
                @in.hand[h].angVel = default;
            }
            var a = r.Avatar();
            Avatar.Sanitize(ref a);
            @in.avatar = a;
        }

        // waiting players in ROOM packets: poses and look only
        static void WritePose(Writer w, PlayerInput @in)
        {
            w.Pose(@in.head); w.Bool(@in.headTracked);
            for (int h = 0; h < 2; ++h) { w.Pose(@in.hand[h].grip); w.Bool(@in.hand[h].tracked); }
            w.Avatar(@in.avatar);
        }

        static void ReadPose(Reader r, PlayerInput @in)
        {
            @in.head = r.Pose(); @in.headTracked = r.U8() != 0;
            for (int h = 0; h < 2; ++h) { @in.hand[h].grip = r.Pose(); @in.hand[h].tracked = r.U8() != 0; }
            var a = r.Avatar();
            Avatar.Sanitize(ref a);
            @in.avatar = a;
        }

        static void Header(Writer w, byte type, uint session, uint seq) { w.U32(kMagic); w.U8(kVersion); w.U8(type); w.U32(session); w.U32(seq); }

        static void WriteRoom(Writer w, RoomInfo ri, uint seq)
        {
            w.U32(seq);
            for (int p = 0; p < kWireSlots; ++p) w.U32(p < ri.slot.Length ? ri.slot[p] : 0);   // (protocol 9: four places)
            w.U8(ri.botMask);                                                                    // the bots' places (bit p)
            int nm = Math.Min(ri.members.Count, 8);
            w.U8((byte)nm);
            for (int i = 0; i < nm; ++i)
            {
                var m = ri.members[i];
                w.U32(m.id); w.U8(m.role); w.I8(m.queuePos); w.U8(m.spot); WritePose(w, m.@in);
            }
            int nw = Math.Min(ri.watchers.Count, 24);
            w.U8((byte)nw);
            for (int i = 0; i < nw; ++i) { var s = ri.watchers[i]; w.U32(s.id); w.U8(s.where); w.V3(s.pos); }
            int nn = Math.Min(ri.names.Count, 16);
            w.U8((byte)nn);
            for (int i = 0; i < nn; ++i) { w.U32(ri.names[i].id); WriteName(w, ri.names[i].name); }
        }

        // a name from the network: 16 bytes, upper-case letters, digits and the keyboard's symbols only
        static string ReadName(Reader r)
        {
            var c = new char[16];
            for (int i = 0; i < 16; ++i) c[i] = (char)r.U8();
            for (int i = 0; i < 16; ++i) if (c[i] != 0 && (c[i] < 32 || c[i] > 'Z')) c[i] = ' ';
            return CStr(c);
        }

        static string CStr(char[] c) { int n = Array.IndexOf(c, '\0'); return new string(c, 0, n < 0 ? c.Length : n); }

        static void WriteName(Writer w, string name)
        {
            for (int i = 0; i < 16; ++i) w.U8(i < name.Length ? (byte)name[i] : (byte)0);
        }

        static bool IsFinite(Vec3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        static bool ReadRoom(Reader r, ref RoomInfo ri, ref uint lastSeq)
        {
            uint seq = r.U32();
            var n = new RoomInfo();
            for (int p = 0; p < kWireSlots; ++p) n.slot[p] = r.U32();
            n.botMask = (byte)(r.U8() & 0x0F);
            n.bot = n.IsBot(1);
            byte nm = r.U8();
            for (int i = 0; i < nm && i < 8 && r.ok; ++i)
            {
                var m = new RoomInfo.Member { id = r.U32(), role = r.U8(), queuePos = r.I8(), spot = (byte)(r.U8() & 7) };
                ReadPose(r, m.@in);
                if (m.queuePos > 3) m.queuePos = 3;   // (four queue for a room for four)
                n.members.Add(m);
            }
            byte nw = r.U8();
            for (int i = 0; i < nw && i < 24 && r.ok; ++i)
            {
                var s = new RoomInfo.Watcher { id = r.U32(), where = r.U8(), pos = r.V3() };
                if (IsFinite(s.pos)) n.watchers.Add(s);
            }
            if (!r.ok || (lastSeq != 0 && seq <= lastSeq)) return false;
            if (r.p < r.e)
            {   // the names (older builds stop before them)
                byte nn = r.U8();
                for (int i = 0; i < nn && i < 16 && r.ok; ++i)
                {
                    var x = new RoomInfo.Name { id = r.U32() };
                    x.name = ReadName(r);
                    if (r.ok) n.names.Add(x);
                }
            }
            if (r.ok && r.p < r.e)
            {   // (hosts from 0.3.24, online: the people's virtual addresses, MUTED PLAYERS)
                byte na = r.U8();
                for (int i = 0; i < na && i < 16 && r.ok; ++i)
                {
                    var x = new RoomInfo.Addr { id = r.U32(), vip = r.U32() };
                    if (r.ok && x.id != 0 && x.vip != 0) n.addrs.Add(x);
                }
            }
            lastSeq = seq;
            ri = n;
            return true;
        }

        // ---------------------------------------------------------------- the public state
        public struct CareerStats
        {
            public bool known;           // the server has (or will keep) stats for this player
            public string name;
            public uint matches, wins, throws, onTarget, hits, blocks, faced, catches;
            public float topSpeed;       // m/s
        }

        // a match's statistics: the rules' MatchTally (Sim/Stats.cs), as natively (Net::MatchStats = MatchTally)

        public class LiveInfo
        {
            public byte state;
            public byte[] score = new byte[4], nation = new byte[2];
            public byte[,] rgb = new byte[2, 3];
            public byte flags;   // 1 advanced, 2 bot, 4 HOTDOG (no shields), 8 free play, 16 knuckle guards on, 32 a round timer
            public float clock;
            public string[] name = { "", "" };
            public MatchTally[] st = new MatchTally[4];   // this match so far (for the fans' stats table; four players: all four)
            public byte round, overtime;
            // FOUR PLAYERS (TASKS.md E47, native 530f6fb, 0.3.23: FOUR CORNERS and FIREBALL BLITZ on The Crowd's page): np 4,
            // the room (2 FOUR CORNERS, 3 FIREBALL BLITZ), its teams, and each player's name, country, colour, team, points,
            // health and whether out
            public byte np = 2, room, teamMode, theOne;
            public string[] name4 = { "", "", "", "" };
            public byte[] nation4 = new byte[4], team4 = new byte[4], health4 = new byte[4], out4 = new byte[4];
            public byte[,] rgb4 = new byte[4, 3];
            public short[] points4 = new short[4];
        }

        public struct Person { public uint id; public string name; public byte role; }
        public const byte VOICE_PLAYER = 0, VOICE_WAITING = 1, VOICE_SPECTATOR = 2;
        public const int kVoiceSamples = 640;   // 40 ms of 16 kHz mu-law
        public class VoiceFrame { public uint speaker; public byte role; public byte[] data = new byte[kVoiceSamples]; }
        public struct SentEvent { public uint id; public double t; public GameEvent e; }
        public const uint kMyStats = 0xFFFFFFFFu;

        // ---------------------------------------------------------------- private state
        class Member   // host: someone in the room (the host itself is one, local = true)
        {
            public uint ip, id;
            public double seen;
            public uint lastSeq;
            public PlayerInput @in = new PlayerInput();
            public sbyte queuePos = -1;
            public byte spot;
            public bool local;
            public string name = "";
            public double active;                    // last moved or pressed something (IDLE ROOMS)
            public readonly Vec3[] @ref = new Vec3[3];   // where head and hands were then
            public byte refButtons;
            public byte refTracked;                  // ... which of head, left, right hand were tracked (bits 0-2)
            public readonly ushort[] moves = new ushort[4];   // diagnostics: what counted as moving since the last idle line (Stir*)
            public ushort lost, found;               // ... head tracking lost / found since then
            public bool back = true;                 // (a room taken over: heard from since)
            public float rttMs = -1;                 // host: the round trip to this player, smoothed (CATCHES ONLINE)
            // HAND TIMES (native 0.3.19): their headset's clock against ours, and when their latest input was taken, on
            // our clock (-1: they don't send their clock)
            public double clockOff, clockAt, sampleAt = -1;
            public int ver;                          // their game's version (0.3.20 = 3020; 0: before 0.3.20, which don't say)
            public bool clockSet;
        }

        class Spectator { public uint ip, id; public double seen; public byte where; public Vec3 pos; public string name = ""; }
        class Removed { public uint id, ip; public string name = ""; }
        class Heir { public uint id, ip; public string name = ""; }

        // HOST HANDOFF and IDLE ROOMS (native 0.3.13)
        List<Heir> heirs = new List<Heir>();      // client: who hosts next, in order
        byte heirFlags;                           // 1 bot fills in, 2 private talk, 4 THE ONE takes turns, 8 THE ONE fixed
        string heirRoom = "";                     // the room's name
        int heirAt;
        bool migrating, inherited, resumed, ownTalk, ownBotFill = true, oneTurns;
        double migrateUntil, resumeWait, lastHeirs, lastExchange, lastClosingWarn;
        double promotedAt;                        // host: when we took the room over (0: we opened it)
        double scoutUntil;                        // client, woken up: looking for where our room went (its new host's beacon)
        double wokeAt;                            // client: when we woke from sleep and asked our host to let us back in
        double migrateFrom;                       // client: when the handover began (beacons heard since then count)
        List<Heir> heirRemoved = new List<Heir>();   // client: the people the host removed (they stay out under the next host)
        uint prevHost;                            // host: the room's previous host (beacons); client: the host we left
        uint prevHostId;
        string roomName = "QUEST";                // host: the room's name (its first host's)
        double awayAfter = 120.0, idleClose = 600.0;
        float pingAvg = -1;                       // BALL LOOK-AHEAD: the round trip (ms), smoothed
        bool lookAhead = true;                    // (on by default from 0.3.21: owner, 2026-11-17, after a player's lag report;
                                                  // it was off while it was tried, owner 2026-11-05)
        double stateAt;                           // when the latest snapshot came (Net's clock)
        double hostOff;                           // the host's game clock minus ours, from its quickest snapshot
        bool hostOffSet;
        double lastIdleLog;                       // (the host's "idle check" line, once a minute: native 0.3.16)
        double deafUntil;
        bool privateTalk;
        string app = "";                          // this build's version, told to the relay in HELLO (its log)

        Socket sock;
        Mode mode = Mode.Offline;
        double now, searchStart, lastBeacon, peerSeen, lastRoomSent, rotateAt, lastSlowState;
        uint peerIp;                   // client: the host
        string latest = "";
        readonly byte[] statsKey = new byte[16];
        bool keySent;
        double lastTank;
        uint statsHost;
        readonly CareerStats[] stats = new CareerStats[2];
        LiveInfo live = new LiveInfo();
        bool liveSet;
        double lastLive;
        CrowdInfo crowd = new CrowdInfo();   // host / client: our room's fans
        uint peerSession;              // client: the host's session id: packets from anyone else are ignored
        uint forfeitId;                // host: a tank player who gave up (handled on the next exchange)
        bool online;
        uint serverIp;                 // network order (s_addr)
        ushort serverPort;
        IPEndPoint serverEp;
        readonly byte[] token = new byte[16];
        uint vip;                      // our virtual address on the relay
        double lastHello, lastRelayPing, lastRelayHeard, lastJoin;
        bool botFill = true, joinPlay = true;
        string serverName = "";
        int otherVer;
        double otherVerSeen = -10;
        uint seq, lastRecvSeq, lastRoomSeq;
        uint statRecv, statLost, statLate;
        float pingMin, pingMax, lastPingMs = -1;
        double pingSum, lastPingSent;
        uint pingN;
        double statStart;
        uint session;
        readonly List<Member> members = new List<Member>();
        readonly List<Removed> removed = new List<Removed>();
        readonly List<(uint target, uint voter)> kickVotes = new List<(uint, uint)>();
        readonly List<uint> myKickVotes = new List<uint>();
        uint kickNow;
        bool kickByHost;
        readonly uint[] slot = { 0, 0, 0, 0 };   // host: member id in each tank slot (0 = empty / bot)
        int np_ = 2;                    // host: the tank's places this match (the game's players)
        int expRoom_;                   // host: its EXPERIMENTAL room (the status line names it)
        bool oneFixed;                  // host: THE ONE's corner picked by the host (MOVE PLAYERS): it stays
        readonly List<uint> queueIds = new List<uint>();
        RoomInfo room = new RoomInfo();
        byte[] lastState = new byte[0];
        readonly LinkedList<SentEvent> eventLog = new LinkedList<SentEvent>();
        uint nextEventId = 1, lastEventId;
        List<GameEvent> pendingEvents = new List<GameEvent>();
        readonly List<LanGame> games = new List<LanGame>();
        readonly List<Spectator> spectators = new List<Spectator>();
        readonly List<SpecMatch> matches = new List<SpecMatch>();
        readonly List<VoiceFrame> voiceIn = new List<VoiceFrame>();
        string camTeam = "";
        byte[] camBlob = new byte[0];
        uint camFrom;
        bool camFresh;
        double lastSpectatePing;
        byte presWhere;
        Vec3 presPos;
        uint presWatch;
        string name = "QUEST";
        string noticeMsg = "";
        double noticeUntil;
        static uint roomSeqCounter;
        readonly byte[] buf = new byte[2048];

        void ResetStats() { statRecv = statLost = statLate = 0; pingSum = 0; pingN = 0; statStart = now; }
        void CountSeq(uint s)
        {
            if (lastRecvSeq != 0 && s > lastRecvSeq + 1) statLost += s - lastRecvSeq - 1;
            ++statRecv; lastRecvSeq = s;
        }

        // ---------------------------------------------------------------- lifecycle
        public void Init()
        {
            try
            {
                sock = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp) { EnableBroadcast = true, Blocking = false };
                if (Environment.OSVersion.Platform != PlatformID.Win32NT) sock.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                try { sock.Bind(new IPEndPoint(IPAddress.Any, kPort)); }
                catch (SocketException)
                {   // another instance on this machine: online play still works
                    sock.Bind(new IPEndPoint(IPAddress.Any, 0));
                    Log($"net: UDP {kPort} busy, using a free port (online only)");
                }
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {   // Windows reports ICMP "port unreachable" as a connection reset on the next receive: switch that off
                    try { sock.IOControl(-1744830452, new byte[] { 0, 0, 0, 0 }, null); } catch (Exception) { }
                }
            }
            catch (Exception ex) { Log("net: socket failed: " + ex.Message); sock = null; return; }
            var rnd = new Random();
            session = (uint)(MonoNow() * 1000) ^ 0x5A5A1234u ^ (uint)rnd.Next();
            if (session == 0) session = 1;
            bool locked = false;
            try { locked = AcquireMulticastLock(); } catch (Exception) { }
            Log($"net: ready on UDP {kPort} (multicast lock {(locked ? "held" : "unavailable")})");
        }

        public void Shutdown() { try { sock?.Close(); } catch (Exception) { } sock = null; }

        void SendTo(byte[] d, uint ip, ushort port)
        {
            if (online)
            {   // through the relay: [magic][SEND][token][dst virtual address][game packet]
                if (vip == 0 || d.Length > 1200) return;
                var b = new byte[25 + d.Length];
                BitConverter.GetBytes(kRelayMagic).CopyTo(b, 0); b[4] = R_SEND; Array.Copy(token, 0, b, 5, 16);
                BitConverter.GetBytes(ip).CopyTo(b, 21);   // (INADDR_BROADCAST stays 0xFFFFFFFF: everyone)
                Array.Copy(d, 0, b, 25, d.Length);
                RawSend(b, serverEp);
                return;
            }
            RawSend(d, new IPEndPoint(AddrOf(ip), port));
        }

        void SendTo(Writer w, uint ip) => SendTo(w.ToArray(), ip, kPort);

        void RawSend(byte[] d, IPEndPoint to)
        {
            if (sock == null || to == null) return;
            try { sock.SendTo(d, to); } catch (Exception) { }
        }

        void RelaySend(byte type, byte[] body)
        {
            int n = body?.Length ?? 0;
            if (!online || vip == 0 || n > 200) return;
            var b = new byte[21 + n];
            BitConverter.GetBytes(kRelayMagic).CopyTo(b, 0); b[4] = type; Array.Copy(token, 0, b, 5, 16);
            if (n > 0) Array.Copy(body, 0, b, 21, n);
            RawSend(b, serverEp);
        }

        public uint RoomHostVip()
        {
            if (!online || vip == 0) return 0;
            if (mode == Mode.Host) return vip;
            if (mode == Mode.Client) return peerIp;
            return 0;
        }

        public void SetStatsKey(byte[] key) { Array.Copy(key, statsKey, 16); keySent = false; }

        // host, online, two people in the tank; score = points 0, points 1, rounds 0, rounds 1 (the other player
        // confirms). mode: Tunables.StatsMode() (0 standard, 1 HOTDOG, 2 custom): careers keep each mode apart as well
        public void ReportMatch(int winner, MatchTally[] st, byte[] score, int statsMode = 0)
        {
            // (a match taken over from another host: our tally only has its end, so careers don't count it)
            if (mode != Mode.Host || resumed || !online || vip == 0 || np_ != 2 || winner < 0 || winner > 1 || slot[0] == 0 || slot[1] == 0) return;
            var b = new byte[38];
            b[33] = (byte)Math.Max(0, Math.Min(2, statsMode));   // (relays before 0.3.8 read the first 33 bytes)
            BitConverter.GetBytes(st[0].returnHits).CopyTo(b, 34);   // (native 0.3.9: the points from returns; older relays: 34 bytes)
            BitConverter.GetBytes(st[1].returnHits).CopyTo(b, 36);
            Array.Copy(score, 0, b, 29, 4);
            b[0] = (byte)winner;
            for (int k = 0; k < 2; ++k)
            {
                ushort[] v = { st[k].throws, st[k].onTarget, st[k].hits, st[k].blocks, st[k].faced, st[k].catches,
                               (ushort)Math.Min(6000f, Math.Max(0f, st[k].topSpeed * 100f)) };
                for (int i = 0; i < 7; ++i) BitConverter.GetBytes(v[i]).CopyTo(b, 1 + 14 * k + 2 * i);
            }
            RelaySend(R_MATCH, b);
            Log($"stats: match reported (winner slot {winner}, mode {statsMode})");
        }

        // a tank player who is not the host: what it saw
        public void ConfirmMatch(int winner, byte[] score)
        {
            if (mode != Mode.Client || !online || vip == 0 || peerIp == 0 || winner < 0 || winner > 1) return;
            var b = new byte[9];
            BitConverter.GetBytes(peerIp).CopyTo(b, 0);
            b[4] = (byte)winner;
            Array.Copy(score, 0, b, 5, 4);
            RelaySend(R_CONFIRM, b);
            Log($"stats: match confirmed (winner slot {winner})");
        }

        static bool ReadCrowd(byte[] d, int p, int e, CrowdInfo c, double now)
        {
            if (e - p < 12) return false;
            c.fans = BitConverter.ToUInt16(d, p); c.cheers[0] = BitConverter.ToUInt16(d, p + 2); c.cheers[1] = BitConverter.ToUInt16(d, p + 4);
            c.hype[0] = Math.Min(100, (int)d[p + 6]); c.hype[1] = Math.Min(100, (int)d[p + 7]);
            c.waves[0] = BitConverter.ToUInt16(d, p + 8); c.waves[1] = BitConverter.ToUInt16(d, p + 10);
            c.at = now;
            return true;
        }

        // the fans of our room's match (host / client), or of the match at hostIp (spectator); null: none lately
        public CrowdInfo Crowd(uint hostIp = 0)
        {
            CrowdInfo c = null;
            if (mode == Mode.Spectate) { foreach (var m in matches) if (m.ip == hostIp) c = m.crowd; }
            else if (InRoom) c = crowd;
            return c != null && now - c.at < 3.0 ? c : null;
        }

        public void SetLive(LiveInfo li) { live = li; liveSet = true; }
        public void RequestMyStats() => RelaySend(R_MYSTATS, null);
        public void DeleteMyStats() { RelaySend(R_DELETE, null); Log("stats: delete requested"); }
        public void RequestStats(uint hostVip) { if (hostVip != 0) RelaySend(R_STATSREQ, BitConverter.GetBytes(hostVip)); }

        // TRAINING's leaderboards (native 0.3.12, the relay's TRAIN / BOARDREQ / BOARD): a passed lesson's result goes out
        // once we are online with our key (queued until then); a board is asked for and its latest answer read
        public class BoardRow { public string name = ""; public uint value; }
        public class Board
        {
            public int lesson = -1, kind;   // lesson id (0: overall); 0 TIME (ms), 1 ACCURACY (tenths of a percent)
            public List<BoardRow> rows = new List<BoardRow>();
            public int myPlace, players;    // (0: not on it)
            public uint myValue;
            public double at;               // when it came
        }
        readonly List<byte[]> trainQ = new List<byte[]>();
        readonly List<Board> boards = new List<Board>();
        public void SubmitTraining(int lessonId, float seconds, int successes, int attempts)
        {
            if (lessonId < 1 || lessonId > 255 || successes < 0 || attempts < 1) return;
            var b = new byte[8];
            b[0] = (byte)lessonId; b[1] = 1;
            uint ms = (uint)Math.Max(0f, Math.Min(3600f, seconds) * 1000f);
            Array.Copy(BitConverter.GetBytes(ms), 0, b, 2, 4);
            b[6] = (byte)Math.Min(255, successes); b[7] = (byte)Math.Min(255, attempts);
            foreach (var q in trainQ) if (((System.Collections.IStructuralEquatable)q).Equals(b, EqualityComparer<byte>.Default)) return;
            trainQ.Add(b);
        }
        public void RequestBoard(int lessonId, int kind) => RelaySend(R_BOARDREQ, new[] { (byte)lessonId, (byte)(kind != 0 ? 1 : 0) });
        public Board BoardOf(int lessonId, int kind)   // null: no answer yet
        {
            foreach (var x in boards) if (x.lesson == lessonId && x.kind == kind) return x;
            return null;
        }
        public int TrainingQueued => trainQ.Count;

        // SOUND STUDIO (native 0.3.16): a vote (-1, 0 taken back, 1) for a library sound, kept until the community server
        // can take it (the studio runs offline: sent the next time this game is online), the latest per sound
        readonly SortedDictionary<string, int> soundQ = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public bool SendSoundVote(string file, int vote)
        {
            if (string.IsNullOrEmpty(file) || file.Length > 64) return false;
            soundQ[file] = vote < 0 ? -1 : vote > 0 ? 1 : 0;
            return true;
        }
        public bool PendingSoundVote(string file, out int vote) => soundQ.TryGetValue(file, out vote);
        public int SoundVotesQueued => soundQ.Count;

        // the library's catalog and its sounds, over TCP on the server's port (native fetchSoundCatalog / fetchSound;
        // relay: b"PASND1\n", then 1 + the 16-byte key (the catalog: "file\tsize\tsha256\tmy vote\n" lines, then
        // "END\n"), or 2 + a length and a file name (u32 size and the file; 0xFFFFFFFF: no such sound))
        public sealed class LibSound { public string file = "", sha = ""; public uint size; public int vote; }
        static TcpClient TcpOpen(string hostPort, int seconds, out string err)
        {
            string host = hostPort ?? "";
            int port = kRelayPort;
            int c = host.LastIndexOf(':');
            if (c >= 0) { int.TryParse(host.Substring(c + 1), out port); host = host.Substring(0, c); }
            err = "";
            if (host.Length == 0) { err = "NO SERVER SET"; return null; }
            try
            {
                var cl = new TcpClient { SendTimeout = seconds * 1000, ReceiveTimeout = seconds * 1000 };
                var conn = cl.BeginConnect(host.ToLowerInvariant(), port, null, null);
                if (!conn.AsyncWaitHandle.WaitOne(seconds * 1000) || !cl.Connected) { cl.Close(); err = "SERVER NOT REACHABLE"; return null; }
                cl.EndConnect(conn);
                return cl;
            }
            catch (Exception) { err = "SERVER NOT FOUND"; return null; }
        }
        static byte[] TcpAsk(string hostPort, byte[] msg, int max, int seconds, out string err)
        {
            using (TcpClient cl = TcpOpen(hostPort, seconds, out err))
            {
                if (cl == null) return null;
                try
                {
                    var s = cl.GetStream();
                    s.Write(msg, 0, msg.Length);
                    var got = new System.IO.MemoryStream();
                    var buf = new byte[16384];
                    for (int k; got.Length < max && (k = s.Read(buf, 0, buf.Length)) > 0;) got.Write(buf, 0, k);
                    return got.ToArray();
                }
                catch (Exception) { err = "NO ANSWER"; return null; }
            }
        }
        public static bool FetchSoundCatalog(string hostPort, byte[] key, List<LibSound> o, out string err)
        {
            o.Clear();
            var msg = new List<byte>(Encoding.ASCII.GetBytes("PASND1\n")) { 1 };
            for (int i = 0; i < 16; ++i) msg.Add(key != null && i < key.Length ? key[i] : (byte)0);
            byte[] got = TcpAsk(hostPort, msg.ToArray(), 64 * 1024, 8, out err);
            if (got == null) return false;
            string t = Encoding.ASCII.GetString(got);
            if (!t.StartsWith("PASND1\n", StringComparison.Ordinal) || t.Length < 11 || !t.EndsWith("END\n", StringComparison.Ordinal)) { err = "NO SOUND LIBRARY"; return false; }
            foreach (string line in t.Substring(7, t.Length - 11).Split('\n'))
            {
                string[] f = line.Split('\t');
                if (f.Length < 4) continue;
                var ls = new LibSound { file = f[0], sha = f[2] };
                uint.TryParse(f[1], out ls.size);
                int.TryParse(f[3], out ls.vote);
                if (ls.file.Length < 8 || ls.file.IndexOf('/') >= 0 || ls.file.IndexOf('\\') >= 0 || ls.file.Contains("..") ||
                    ls.size == 0 || ls.size > 1400000 || ls.sha.Length != 64)
                    continue;   // (never a path out of the library's folder)
                o.Add(ls);
            }
            err = "";
            return true;
        }
        public static bool FetchSound(string hostPort, string file, out byte[] data, out string err)
        {
            data = null;
            var msg = new List<byte>(Encoding.ASCII.GetBytes("PASND1\n")) { 2 };
            byte[] name = Encoding.ASCII.GetBytes(file ?? "");
            int n = Math.Min(name.Length, 255);
            msg.Add((byte)n);
            for (int i = 0; i < n; ++i) msg.Add(name[i]);
            byte[] got = TcpAsk(hostPort, msg.ToArray(), 1400000 + 4, 15, out err);
            if (got == null) return false;
            if (got.Length < 4) { err = "NO ANSWER"; return false; }
            uint size = BitConverter.ToUInt32(got, 0);
            if (size == 0xFFFFFFFFu) { err = "NOT IN THE LIBRARY"; return false; }
            if (got.Length - 4 != size) { err = "CUT SHORT"; return false; }
            data = new byte[size];
            Array.Copy(got, 4, data, 0, size);
            err = "";
            return true;
        }

        // the latest answer for that host (false: none)
        public bool StatsFor(uint hostVip, CareerStats[] o)
        {
            if (hostVip == 0 || hostVip != statsHost) return false;
            o[0] = stats[0]; o[1] = stats[1];
            return true;
        }

        void RelayHello()
        {
            var b = new byte[38 + 16];   // token: zeros; then this build's version (length + ASCII; relays before 0.3.11 ignore it)
            BitConverter.GetBytes(kRelayMagic).CopyTo(b, 0); b[4] = R_HELLO;
            b[21] = kVersion;
            for (int i = 0; i < 16 && i < name.Length; ++i) b[22 + i] = (byte)name[i];
            b[38] = (byte)app.Length;
            for (int i = 0; i < app.Length; ++i) b[39 + i] = (byte)app[i];
            RawSend(b, serverEp);
            lastHello = now > 0 ? now : MonoNow();
        }

        // "host[:port]"; false if the address does not resolve
        public bool GoOnline(string hostPort)
        {
            string host = hostPort ?? "";
            ushort port = kRelayPort;
            int c = host.LastIndexOf(':');
            if (c >= 0) { ushort.TryParse(host.Substring(c + 1), out port); host = host.Substring(0, c); }
            host = host.ToLowerInvariant();   // typed upper-case in VR
            IPAddress addr = null;
            try
            {
                if (host.Length > 0)
                    foreach (var a in Dns.GetHostAddresses(host))
                        if (a.AddressFamily == AddressFamily.InterNetwork) { addr = a; break; }
            }
            catch (Exception) { }
            if (addr == null) { Notice("SERVER NOT FOUND"); return false; }
            serverIp = IpOf(addr);
            Leave();
            games.Clear();
            serverPort = port != 0 ? port : kRelayPort;
            serverEp = new IPEndPoint(addr, serverPort);
            serverName = hostPort;
            online = true; vip = 0; lastRelayHeard = now;
            RelayHello();
            Log($"net: going online via {serverName}");
            return true;
        }

        public void GoOffline()
        {
            if (!online) return;
            Leave();
            var b = new byte[21];
            BitConverter.GetBytes(kRelayMagic).CopyTo(b, 0); b[4] = R_BYE; Array.Copy(token, 0, b, 5, 16);
            RawSend(b, serverEp);
            online = false; vip = 0; games.Clear();
            Log("net: back on the LAN");
        }

        public bool Online => online;
        public bool OnlineReady => online && vip != 0;
        public string ServerName => serverName;
        public string LatestVersion => latest;   // the current release, as the relay says ("" = not told)
        // a game on the network runs another protocol version (both headsets need the same build); 0 = none seen
        public int OtherVersion => now - otherVerSeen < 3.0 ? otherVer : 0;
        public uint Session => session;
        public uint PeerIp => peerIp;
        public Mode CurrentMode => mode;
        // BALL LOOK-AHEAD (SETTINGS > GRAPHICS AND VIEW; off unless the player turns it on, owner 2026-11-05): not hosting,
        // the balls are drawn a round trip ahead and your throw leaves your hand at once (Game.LookAhead)
        public bool LookAhead { get => lookAhead; set => lookAhead = value; }
        public List<SpecMatch> Matches => matches;
        // a game being played there: a new snapshot in the last 3 s and not waiting for players (native 0.3.16; else
        // the hub says WAITING FOR PLAYERS)
        public bool Live(SpecMatch m) => now - m.stateSeen < 3.0 && m.game.match != MatchState.WaitingForPlayers;
        public int SpectatorCount => spectators.Count;
        public bool BotFill { get => botFill; set => botFill = value; }   // host: the practice bot plays a lone player
        public string Name => name;
        public List<VoiceFrame> VoiceInbox => voiceIn;

        public void SetName(string n)
        {
            name = string.IsNullOrEmpty(n) ? "QUEST" : n.ToUpperInvariant();   // HUD font is upper-case only
            if (name.Length > 16) name = name.Substring(0, 16);
            if (!inherited) roomName = name;               // (our room has our name)
            if (online && serverIp != 0) RelayHello();   // the relay knows us by name (its log, player reports)
        }

        // this build ("0.3.11"): told to the relay in HELLO, for its log (15 characters at most), and (native 0.3.20) to a
        // host at the end of JOIN (FAIR MATCHES)
        public void SetAppVersion(string v)
        {
            app = v ?? "";
            if (app.Length > 15) app = app.Substring(0, 15);
        }

        // "0.3.20" -> 3020 (major * 1000000 + minor * 1000 + patch; "" -> 0), as natively
        public static int VersionNumber(string v)
        {
            int[] part = { 0, 0, 0 };
            int k = 0;
            foreach (char c in v ?? "")
            {
                if (k >= 3) break;
                if (c >= '0' && c <= '9') part[k] = Math.Min(999, part[k] * 10 + (c - '0'));
                else if (c == '.') ++k;
                else break;
            }
            return part[0] * 1000000 + part[1] * 1000 + part[2];
        }

        // host: the oldest game among the match's players (FAIR MATCHES, Tunables.matchVersion); our own build and bots
        // have everything; not hosting: Tunables.kVerAll
        public int MatchVersion()
        {
            if (mode != Mode.Host) return Tunables.kVerAll;
            int v = Tunables.kVerAll;
            for (int p = 0; p < np_ && p < slot.Length; ++p)
            {
                if (slot[p] == 0) continue;   // (empty, or a bot: ours)
                foreach (var m in members)
                    if (m.id == slot[p] && !m.local) v = Math.Min(v, m.ver);
            }
            return v;
        }

        // host: ROOM TALK: PRIVATE (owner, 2026-11-02, "fog of war"): spectators see the match but don't hear the room's
        // voices (the players and those waiting still do)
        public bool PrivateTalk { get => privateTalk; set => privateTalk = value; }
        public bool Inherited => inherited && mode == Mode.Host;   // hosting a room taken over (its rules)
        public bool HandingOver => migrating;                      // between hosts (the match is paused)
        // IDLE ROOMS: a player whose headset sits still (no movement, no buttons) for 2 minutes leaves the tank for the
        // waiting area; a room where nobody has moved for 10 minutes closes, after a minute's warning. Spectators don't
        // keep a room open. (Tests shorten both.)
        public void SetIdleTimes(double away, double close) { awayAfter = away; idleClose = close; }
        public void Deafen(double seconds) => deafUntil = now + seconds;   // (tests: our Wi-Fi drops; nothing arrives for a while)

        public List<LanGame> Games()   // hosts seen in the last 2 s
        {
            var o = new List<LanGame>();
            foreach (var g in games) if (now - g.seen < 2.0) o.Add(g.Copy());
            return o;
        }

        void Notice(string msg) { noticeMsg = msg; noticeUntil = now + 5.0; }
        // woken up: the host still announces its room (a beacon in the last 3 s), or our new session on the relay has not
        // had time to hear one yet; within kWakeKnock of waking. Back in touch with the host: no longer woken (native 8f1fbdc)
        bool WokeAndHostThere()
        {
            if (wokeAt <= 0) return false;
            if (PeerConnected || now - wokeAt > kWakeKnock)
            {
                if (!PeerConnected) Log($"net: back from sleep, the host did not let us back in for {now - wokeAt:F0} s");
                wokeAt = 0;
                return false;
            }
            if (now - wokeAt < kHostQuiet + 4.0) return true;   // (a new relay session: its first beacons take a moment)
            foreach (LanGame lg in games)
                if (lg.ip == peerIp && lg.session == peerSession && now - lg.seen < 3.0) return true;
            return false;
        }
        public void NoticeLocal(string msg) => Notice(msg);
        public string NoticeText => now < noticeUntil ? noticeMsg : null;   // short-lived banner ("PLAYER JOINED", ...) or null

        // lobby: host an arena (beacons with our name); we start in the room's waiting area
        public void Host()
        {
            Leave();
            mode = Mode.Host; peerIp = 0; peerSeen = 0; lastBeacon = 0;
            members.Clear();
            members.Add(new Member { id = session, local = true, seen = now, active = now });
            roomName = name;
            prevHost = 0; resumeWait = 0; resumed = false;
            Array.Clear(slot, 0, slot.Length);   // the host starts in the room's waiting area too: JOIN QUEUE to play (owner request)
            oneFixed = false;
            queueIds.Clear();
            rotateAt = 0;
            HostRoomInfo();
            Notice("ROOM OPEN - JOIN THE QUEUE TO PLAY");
            Log($"net: hosting as \"{name}\"");
        }

        // lobby: enter the arena hosted at ip. play: take the bot's place, or queue (JOIN ROOM: just wait outside)
        public void Join(uint ip, bool play = true)
        {
            foreach (var lg in games)   // (a native EXPERIMENTAL room or a room for four: not in this port yet)
                if (lg.ip == ip && !lg.Playable) { Notice("THAT ROOM NEEDS THE NATIVE GAME FOR NOW"); Log($"net: {lg.name}'s room is not playable here yet"); return; }
            Leave();
            mode = Mode.Client; peerIp = ip; peerSeen = now; lastRecvSeq = 0; lastRoomSeq = 0;
            peerSession = 0;
            foreach (var lg in games) if (lg.ip == ip) peerSession = lg.session;
            lastEventId = 0; pendingEvents.Clear(); ResetStats();
            room = new RoomInfo();
            joinPlay = play;
            SendJoin(play);
            Log($"net: joining host {IpStr(ip)} ({(play ? "play" : "room")})");
        }

        // back to practice vs bot (tells the other side). A host with people still in its room hands it on (HOST HANDOFF)
        public void Leave()
        {
            if (mode == Mode.Client && peerIp != 0)
            {   // tell the host right away
                var w = new Writer(); Header(w, MSG_LEAVE, session, 0);
                SendTo(w, peerIp);
            }
            if (mode == Mode.Host)
            {   // people still here: the room goes on with the next host
                uint next = 0;   // (someone who is here: not one still finding a room we took over)
                foreach (var m in members) if (!m.local && m.back && next == 0) next = m.id;
                if (next != 0) HandOver(next, false);
                else CloseRoom();
            }
            if (mode == Mode.Host || mode == Mode.Client || mode == Mode.Spectate) Log("net: offline");
            StepDown();
            DropRoom(null);
            matches.Clear();
        }

        void CloseRoom()
        {   // host: tell everyone in the room the arena is closing
            var w = new Writer(); Header(w, MSG_LEAVE, session, 0);
            byte[] d = w.ToArray();
            foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
        }

        // watch every live arena (leaves any room first)
        public void Spectate()
        {
            Leave();
            mode = Mode.Spectate;
            matches.Clear();
            lastSpectatePing = 0;
            Log("net: spectating");
        }

        // Offline <-> LAN (search, then host or join)
        public void ToggleLan()
        {
            if (mode == Mode.Offline) { mode = Mode.Searching; searchStart = MonoNow(); peerSeen = 0; Log("net: searching for host"); }
            else Leave();
        }

        // in the tank: give up the match and go back to the room's waiting area (QUEUE with 2; older hosts read it as a
        // queue request, which does nothing for a tank player)
        public void Forfeit()
        {
            if (mode == Mode.Client) { var w = new Writer(); Header(w, MSG_QUEUE, session, 0); w.U8(2); SendTo(w, peerIp); }
            else if (mode == Mode.Host) forfeitId = session;
            Log("net: forfeit");
        }

        // waiting: join / leave the arena's queue
        public void Queue(bool want)
        {
            if (mode == Mode.Client) { var w = new Writer(); Header(w, MSG_QUEUE, session, 0); w.Bool(want); SendTo(w, peerIp); }
            else if (mode == Mode.Host)
            {
                int at = queueIds.IndexOf(session);
                if (want && at < 0 && !InTank(session) && queueIds.Count < np_) queueIds.Add(session);
                if (!want && at >= 0) queueIds.RemoveAt(at);
                HostRoomInfo();   // the tank fills on the next exchange
            }
        }

        public bool InRoom => mode == Mode.Host || mode == Mode.Client;
        public RoomInfo Room => room;

        // 0 .. 3 in the tank; -1 waiting outside (or not in a room = 0 offline)
        public int LocalSlot()
        {
            if (mode == Mode.Host) { for (int p = 0; p < kWireSlots; ++p) if (slot[p] == session) return p; return -1; }
            if (mode == Mode.Client) { for (int p = 0; p < kWireSlots; ++p) if (room.slot[p] == session) return p; return -1; }
            return 0;
        }

        public int LocalIndex() { int s = LocalSlot(); return s < 0 ? 0 : s; }
        public bool LocalWaiting() => InRoom && LocalSlot() < 0;

        public int LocalQueuePos() { foreach (var m in room.members) if (m.id == session) return m.queuePos; return -1; }
        public int LocalSpot() { foreach (var m in room.members) if (m.id == session) return m.spot; return 0; }

        // a human or the bot is in tank slot p
        public bool SlotFilled(int p) { if (!InRoom) return true; return room.slot[p] != 0 || room.IsBot(p); }

        // spectators report where they are (blue markers): where 0 = lobby (pos in lobby space), 1 = near `watching`
        public void SetPresence(byte where, Vec3 pos, uint watching) { presWhere = where; presPos = pos; presWatch = watching; }

        public bool PeerConnected => peerSeen > 0 && (now - peerSeen) < 3.0;
        public float PingMs => InRoom ? lastPingMs : -1f;   // the latest round trip to the host / a player; -1 none
        public uint HostSession => mode == Mode.Host ? session : mode == Mode.Client ? peerSession : 0;

        public string Status()
        {
            string where = online ? "ONLINE" : "LAN";
            switch (mode)
            {
                case Mode.Offline: return "PRACTICE VS BOT";
                case Mode.Searching: return "SEARCHING LAN...";
                case Mode.Spectate: return "SPECTATING";
                default:
                {
                    if (mode == Mode.Client && migrating) return $"{where} - CHANGING HOST";
                    if (mode == Mode.Client && !PeerConnected) return $"{where} - HOST NOT RESPONDING";
                    int s = LocalSlot(), q = LocalQueuePos();
                    int waiting = 0;
                    foreach (var m in room.members) waiting += m.role == RoomInfo.Waiting ? 1 : 0;
                    int others = 0;
                    for (int p = 0; p < kWireSlots; ++p) others += p != s && room.slot[p] != 0 ? 1 : 0;
                    if (s >= 0 && np_ == 4)
                        return $"{where} {(mode == Mode.Host ? "HOST" : "ARENA")} - {(expRoom_ == Sim.Room.Blitz ? "FIREBALL BLITZ" : "FOUR CORNERS")}" +
                               (waiting != 0 ? " - PLAYERS WAITING" : "");
                    if (s >= 0)
                        return $"{where} {(mode == Mode.Host ? "HOST" : "ARENA")} - " +
                               (others != 0 ? "VS PLAYER" : room.bot ? "VS BOT" : "WAITING FOR AN OPPONENT") +
                               (waiting != 0 ? " - PLAYERS WAITING" : "");
                    if (q >= 0) return $"{where} - QUEUED {(q == 0 ? "NEXT" : q == 1 ? "SECOND" : q == 2 ? "THIRD" : "FOURTH")}";
                    return $"{where} - WAITING OUTSIDE THE TANK";
                }
            }
        }

        // ---------------------------------------------------------------- snapshot
        static void WriteState(Writer w, Game g)
        {
            // protocol 9 / 10, as the native writeState: the match, then the rules (MATCH OPTIONS and the EXPERIMENTAL
            // room, its players and teams), then each tank player compactly, then FIREBALL BLITZ's block, then (0.3.14,
            // two players) the shot clocks.
            w.U8((byte)g.match); w.F32(g.stateTimer); w.F32(g.roundClock); w.I16((short)g.round);
            w.I8((sbyte)g.lastRoundWinner); w.I8((sbyte)g.matchWinner); w.Bool(g.overtime); w.Bool(g.tun.advanced);
            w.Bool(g.tun.shields); w.I8((sbyte)g.tun.knuckleMode); w.F32(g.tun.basicMatchTime); w.F32(g.tun.advancedRoundTime);
            w.Bool(g.tun.experimental); w.U8((byte)g.tun.expRoom); w.U8((byte)g.np);
            w.U8((byte)g.tun.teamMode); w.U8((byte)g.tun.theOne);
            for (int p = 0; p < g.np; ++p)
            {
                PlayerState s = g.pl[p];
                WriteTankInput(w, s.@in);
                w.U8((byte)((s.hands[0].shieldUp ? 1 : 0) | (s.hands[1].shieldUp ? 2 : 0) | (s.hands[0].knuckleUp ? 4 : 0) |
                            (s.hands[1].knuckleUp ? 8 : 0) | (s.ball.charged ? 16 : 0) | (s.ball.scored ? 32 : 0) | (s.isBot ? 64 : 0) |
                            (s.ready ? 128 : 0)));   // (READY UP, native 0.3.14)
                w.I16((short)s.score); w.U8((byte)s.roundWins); w.U8((byte)s.strikesAgainst);
                w.U8((byte)s.strikesFor);
                w.U8((byte)s.ball.state); w.V3(s.ball.pos); w.V3(s.ball.vel); w.I8((sbyte)s.ball.heldHand);
                w.I8((sbyte)s.ball.instigator); w.F32(s.ball.scale);
            }
            if (g.tun.experimental && g.tun.expRoom == Sim.Room.Blitz)
            {   // FIREBALL BLITZ: everyone's health, who started and who stands in the ring, when it closes, its options, and the
                // fireballs in the room (centimetres and centimetres a second: a full room of them fits the relay's 1200 bytes)
                for (int p = 0; p < g.np; ++p) w.U8((byte)Math.Max(0, Math.Min(255, g.pl[p].health)));
                w.U8(g.startMask); w.U8(g.ringMask);
                w.I16((short)(g.ringCloseAt < 0 ? -1 : LRound(Math.Max(0f, g.ringCloseAt - g.now) * 100f)));
                w.U8((byte)g.tun.health); w.U8((byte)g.tun.fireBalls);
                for (int p = 0; p < g.np; ++p)
                {
                    byte m = 0;
                    for (int k = 0; k < Game.KMaxFire; ++k) m |= g.fire[p][k].state == BallState.Flying ? (byte)(1u << k) : (byte)0;
                    w.U8(m);
                    for (int k = 0; k < Game.KMaxFire; ++k)
                    {
                        Ball f = g.fire[p][k];
                        if (f.state != BallState.Flying) continue;
                        w.I16(Cm(f.pos.x)); w.I16(Cm(f.pos.y)); w.I16(Cm(f.pos.z));
                        w.I16(Cm(f.vel.x)); w.I16(Cm(f.vel.y)); w.I16(Cm(f.vel.z));
                        w.I8((sbyte)f.instigator);
                    }
                }
            }
            if (g.np == 2)
                for (int p = 0; p < 2; ++p)   // the shot clocks, tenths of a second (the ring round each launchpad)
                    w.U8((byte)Math.Clamp((int)MathF.Round(g.pl[p].shotClock * 10f, MidpointRounding.AwayFromZero), 0, 255));
            // (native 0.3.15, BALL LOOK-AHEAD) what a player who isn't hosting needs to fly the balls on as the host will:
            // each ball's spin (hundredths of a rad/s; curves), its bounces and whether it is fouled / going home. Older
            // builds stop before it
            if (g.np == 2)
                for (int p = 0; p < 2; ++p)
                {
                    Ball b = g.pl[p].ball;
                    w.I16(Cm(b.spin.x)); w.I16(Cm(b.spin.y)); w.I16(Cm(b.spin.z));
                    w.U8((byte)((b.fouled ? 1 : 0) | (b.homing ? 2 : 0) | (Math.Min(b.bounces, 63) << 2)));
                }
            if (g.np == 2) w.F32(g.now);   // (... and the host's game clock: how far to look ahead, steady)
            // (the rules this match plays: an option an older player's game lacks goes out at regulation, FAIR MATCHES)
            if (g.np == 2) w.U8((byte)g.tun.StrikeSpeedNow());   // (native 0.3.19: STRIKE SPEED)
            if (g.np == 2) w.I8((sbyte)g.tun.FoulShiftNow());     // (native 0.3.20: FOUL LINE)
            if (g.np == 2)   // (native 0.3.21, ROOMS AND THEIR OPTIONS: MAIDEN, AIM ASSIST's step, STRIKE ZONE, BALL GROWTH, as they play)
                w.U8((byte)((g.tun.MaidenNow() ? 1 : 0) | (g.tun.AimAssistNow() << 1) | (g.tun.StrikeZoneNow() << 3) | (g.tun.BallGrowthNow() << 5)));
            if (g.np == 2) w.U8((byte)g.tun.RoundsToWinNow());   // (native 0.3.21: ROUNDS, rounds to win: BEST OF 1 to 9)
            if (g.np == 2) w.U8((byte)g.tun.ShieldSizeNow());    // (native 0.3.21: SHIELD SIZE, 0 regulation .. 4 double)
            if (g.np == 2) w.U8((byte)(g.tun.ballFeel & Tunables.BALL_FEEL_ALL));   // (native 0.3.21: BALL FEEL, BALL_* bits)
            // (native 0.3.22, BOUNCES: WALLS, WALL GRIP and BANK ASSIST as they play; with BANK ASSIST, each ball's turn still
            // to come, so a player looking ahead turns it as the host will)
            if (g.np == 2)
            {
                int bank = g.tun.BankAssistNow();
                w.U8((byte)((g.tun.WallsLivelyNow() ? 1 : 0) | (g.tun.WallGripNow() ? 2 : 0) | (bank << 2)));
                if (bank > 0)
                    for (int p = 0; p < 2; ++p)
                    {
                        Ball bb = g.pl[p].ball;
                        w.U8((byte)(bb.bankAimPending ? 1 : 0));
                        foreach (float v in new[] { bb.bankTurn.x, bb.bankTurn.y, bb.bankTurn.z })   // (ten-thousandths of a radian)
                            w.I16((short)System.Math.Max(-32000L, System.Math.Min(32000L, (long)System.Math.Round(v * 10000f, System.MidpointRounding.AwayFromZero))));
                    }
            }
            w.U8((byte)(g.tun.PalmHoldNow() ? 1 : 0));   // (0.3.22, any room: THE BALL IN THE LEFT PALM, as this match plays it; TASKS.md E43)
            // (0.3.24, BALL LOOK-AHEAD with four players: FOUR CORNERS; TASKS.md E48, native 8ea32fc) what two-player rooms
            // have sent since 0.3.15, for all four balls: each one's spin, bounces and whether it is fouled / going home, and
            // the host's game clock. After the last byte older builds read, so they stop before it (FIREBALL BLITZ: not yet;
            // its fireballs fill the packet)
            if (g.np == 4 && !(g.tun.experimental && g.tun.expRoom == Sim.Room.Blitz))
            {
                for (int p = 0; p < 4; ++p)
                {
                    Ball b = g.pl[p].ball;
                    w.I16(Cm(b.spin.x)); w.I16(Cm(b.spin.y)); w.I16(Cm(b.spin.z));
                    w.U8((byte)((b.fouled ? 1 : 0) | (b.homing ? 2 : 0) | (Math.Min(b.bounces, 63) << 2)));
                }
                w.F32(g.now);
            }
            // (0.3.24, BALL LOOK-AHEAD in FIREBALL BLITZ; TASKS.md E49, native b289ab9) each flying fireball's spin round the
            // up axis (quarter rad/s: what curves it along the floor) and its bounces (it goes out at fireBounces), in the order
            // of the fireballs above, then the host's game clock: 28 bytes with twelve
            if (g.tun.experimental && g.tun.expRoom == Sim.Room.Blitz)
            {
                for (int p = 0; p < g.np; ++p)
                    for (int k = 0; k < Game.KMaxFire; ++k)
                    {
                        Ball f = g.fire[p][k];
                        if (f.state != BallState.Flying) continue;
                        w.I8((sbyte)Math.Max(-127L, Math.Min(127L, LRound(f.spin.y * 4f))));
                        w.U8((byte)Math.Max(0, Math.Min(255, f.bounces)));
                    }
                w.F32(g.now);
                // (0.3.24, FIREBALL BLITZ in teams, TASKS.md E51) the teams the room plays: 0 everyone for themselves, 1 2 v 2,
                // 2 3 v 1 (with an older player in it, everyone for themselves). The byte before it, the room's own TEAMS, an
                // older host sends for every room, FIREBALL BLITZ too, where it never played: so only this one says FIREBALL
                // BLITZ plays in teams
                w.U8((byte)g.TeamsNow());
            }
        }

        static long LRound(float v) => (long)MathF.Round(v, MidpointRounding.AwayFromZero);   // (native std::lround)
        static short Cm(float v) => (short)Math.Max(-32000L, Math.Min(32000L, LRound(v * 100f)));

        // Events ride at the front of every STATE packet: the last ~0.3 s of events with consecutive ids, so a lost or
        // overtaken snapshot does not drop a throw / hit; the client keeps only ids it has not seen.
        static void WriteEvents(Writer w, LinkedList<SentEvent> log)
        {
            int n = Math.Min(log.Count, 24);
            var node = log.First;
            for (int skip = log.Count - n; skip > 0; --skip) node = node.Next;
            w.U32(n != 0 ? node.Value.id : 0); w.U8((byte)n);
            for (; node != null; node = node.Next)
            {
                GameEvent e = node.Value.e;
                w.U8((byte)e.type); w.I8((sbyte)e.player); w.I8((sbyte)e.hand); w.V3(e.pos); w.F32(e.value);
            }
        }

        public static bool ReadState(Reader r, Game g, int localIndex)
        {
            g.match = (MatchState)r.U8(); g.stateTimer = r.F32(); g.roundClock = r.F32(); g.round = r.I16();
            g.lastRoundWinner = r.I8(); g.matchWinner = r.I8(); g.overtime = r.U8() != 0; g.tun.advanced = r.U8() != 0;
            g.tun.shields = r.U8() != 0;
            g.tun.knuckleMode = Math.Max(-1, Math.Min(1, (int)r.I8()));
            float mt = r.F32(), rt = r.F32();
            if (float.IsFinite(mt) && mt >= 0 && mt <= 3600) g.tun.basicMatchTime = mt;
            if (float.IsFinite(rt) && rt >= 0 && rt <= 3600) g.tun.advancedRoundTime = rt;
            bool experimental = r.U8() != 0;
            int expRoom = Math.Min((int)r.U8(), Sim.Room.Count - 1);
            int np = r.U8() == 4 ? 4 : 2;
            int teamMode = Math.Min(2, (int)r.U8()), theOne = r.U8() & 3;
            if (!r.ok) return false;
            g.tun.experimental = experimental; g.tun.expRoom = expRoom; g.tun.teamMode = teamMode; g.tun.theOne = theOne;
            g.np = np;
            g.tun.players = np;
            var scratch = new PlayerInput();
            for (int p = 0; p < np; ++p)
            {
                PlayerState s = g.pl[p];
                ReadTankInput(r, scratch);
                if (p != localIndex) s.@in.CopyFrom(scratch);   // keep our own latest local poses for latency-free hands
                byte f = r.U8();
                s.hands[0].shieldUp = (f & 1) != 0; s.hands[1].shieldUp = (f & 2) != 0;
                s.hands[0].knuckleUp = (f & 4) != 0; s.hands[1].knuckleUp = (f & 8) != 0;
                s.ball.charged = (f & 16) != 0; s.ball.scored = (f & 32) != 0; s.isBot = (f & 64) != 0; s.ready = (f & 128) != 0;
                s.score = r.I16(); s.roundWins = r.U8(); s.strikesAgainst = r.U8(); s.strikesFor = r.U8();
                s.ball.state = (BallState)Math.Min(r.U8(), (byte)BallState.Returning);
                s.ball.pos = r.V3(); s.ball.vel = r.V3(); s.ball.heldHand = r.I8();
                if (s.ball.heldHand < -1 || s.ball.heldHand > 1) s.ball.heldHand = -1;
                s.ball.instigator = r.I8(); s.ball.scale = r.F32();
                if (!float.IsFinite(s.ball.scale) || s.ball.scale < 0.1f || s.ball.scale > 10f) s.ball.scale = 1f;
            }
            if (experimental && expRoom == Sim.Room.Blitz)
            {   // FIREBALL BLITZ
                for (int p = 0; p < np; ++p) g.pl[p].health = r.U8();
                g.startMask = (byte)(r.U8() & 15);
                byte ring = (byte)(r.U8() & 15);
                short closeIn = r.I16();
                g.ringCloseAt = closeIn < 0 ? -1f : g.now + closeIn / 100f;
                g.tun.health = Math.Max(1, Math.Min(10, (int)r.U8()));
                g.tun.fireBalls = Math.Max(1, Math.Min(4, (int)r.U8()));
                if (r.ok && ring != g.ringMask) { g.ringMask = ring; g.LayoutRing(ring); }   // (the room's layout as the host has it)
                for (int p = 0; p < np && r.ok; ++p)
                {
                    byte m = r.U8();
                    for (int k = 0; k < Game.KMaxFire; ++k)
                    {
                        Ball f = g.fire[p][k];
                        if (((m >> k) & 1) == 0) { f.state = BallState.Dead; continue; }
                        Vec3 pos, vel;
                        pos.x = r.I16() / 100f; pos.y = r.I16() / 100f; pos.z = r.I16() / 100f;
                        vel.x = r.I16() / 100f; vel.y = r.I16() / 100f; vel.z = r.I16() / 100f;
                        f.instigator = r.I8();
                        f.state = BallState.Flying; f.pos = pos; f.vel = vel; f.scale = 1f;
                        f.spin = default; f.bounces = 0; f.timer = 0;   // (hosts from 0.3.24 send the spin and bounces below)
                        f.speedLimit = Math.Max(5f, MathF.Sqrt(vel.x * vel.x + vel.z * vel.z));   // (read ahead for its warning)
                    }
                }
            }
            else if (g.ringMask != 0)
            {   // out of FIREBALL BLITZ: everyone back to the room's own places (native b298cc7: a spectator's copy of the
                // match kept the ring's, and its DIAMOND drew the platforms there, away from the players; owner, 2026-11-16)
                var def = new Tunables();
                for (int p = 0; p < g.tun.start.Length && p < def.start.Length; ++p) { g.tun.start[p] = def.start[p]; g.tun.spawner[p] = def.spawner[p]; }
                g.ringMask = 0;
            }
            if (np == 2 && r.ok && r.e - r.p >= 2)  // the shot clocks (hosts from native 0.3.14)
                for (int p = 0; p < 2; ++p) g.pl[p].shotClock = r.U8() / 10f;
            for (int p = 0; p < np; ++p)
            {   // (a ball's own speed: what flying it on keeps; snapshots carry its velocity)
                Ball b = g.pl[p].ball;
                b.speedLimit = Sim.M.Length(b.vel);
                b.spin = default;
                b.fouled = b.homing = false;
                b.bounces = 0;
            }
            if (np == 2 && r.ok && r.e - r.p >= 14)   // spin, bounces, fouled (hosts from native 0.3.15: BALL LOOK-AHEAD)
                for (int p = 0; p < 2; ++p)
                {
                    Ball b = g.pl[p].ball;
                    b.spin.x = r.I16() / 100f; b.spin.y = r.I16() / 100f; b.spin.z = r.I16() / 100f;
                    byte f = r.U8();
                    b.fouled = (f & 1) != 0; b.homing = (f & 2) != 0; b.bounces = f >> 2;
                }
            g.ahead.snapNow = -1f;
            if (np == 2 && r.ok && r.e - r.p >= 4)
            {
                float hn = r.F32();
                if (r.ok && float.IsFinite(hn) && hn >= 0f) g.ahead.snapNow = hn;
            }
            g.tun.strikeSpeed = 0;   // (STRIKE SPEED, hosts from native 0.3.19; older hosts: regulation)
            if (np == 2 && r.ok && r.e - r.p >= 1) g.tun.strikeSpeed = System.Math.Min(2, (int)r.U8());
            g.tun.foulShift = 0;     // (FOUL LINE, hosts from native 0.3.20; older hosts: where the arena has it)
            if (np == 2 && r.ok && r.e - r.p >= 1) g.tun.foulShift = System.Math.Max(-2, System.Math.Min(2, (int)r.I8()));
            g.tun.maiden = false; g.tun.aimAssist = -1; g.tun.strikeZone = 1; g.tun.ballGrowth = 3;   // (ROOMS AND THEIR OPTIONS,
            if (np == 2 && r.ok && r.e - r.p >= 1)                                                   // hosts from 0.3.21; older: the rules')
            {
                int o = r.U8();
                g.tun.maiden = (o & 1) != 0;
                g.tun.aimAssist = (o >> 1) & 3;
                g.tun.strikeZone = System.Math.Min(2, (o >> 3) & 3);
                g.tun.ballGrowth = (o >> 5) & 3;
            }
            g.tun.advancedRoundsToWin = 2;   // (ROUNDS, hosts from 0.3.21; older: BEST OF 3)
            if (np == 2 && r.ok && r.e - r.p >= 1) g.tun.advancedRoundsToWin = System.Math.Max(1, System.Math.Min(5, (int)r.U8()));
            g.tun.shieldSize = 0;   // (SHIELD SIZE, hosts from 0.3.21; older: regulation)
            if (np == 2 && r.ok && r.e - r.p >= 1) g.tun.shieldSize = System.Math.Min(4, (int)r.U8());
            // (BALL FEEL, hosts from 0.3.21: the host's ball for everyone at its arena; an older host sends none, and a guest
            // keeps its own: only how the ball looks and feels)
            if (np == 2 && r.ok && r.e - r.p >= 1) g.tun.ballFeel = r.U8() & Tunables.BALL_FEEL_ALL;
            g.tun.wallsLively = g.tun.wallGrip = false; g.tun.bankAssist = 0;   // (BOUNCES, hosts from 0.3.22; older: STANDARD)
            if (np == 2 && r.ok && r.e - r.p >= 1)
            {
                int o = r.U8();
                g.tun.wallsLively = (o & 1) != 0;
                g.tun.wallGrip = (o & 2) != 0;
                g.tun.bankAssist = (o >> 2) & 3;
            }
            for (int p = 0; p < np && p < 2; ++p)
            {   // (BANK ASSIST: each ball's turn still to come, the host's)
                Ball bb = g.pl[p].ball;
                bb.bankAimPending = false;
                bb.bankTurn = default;
                if (np != 2 || g.tun.bankAssist == 0 || !r.ok) continue;
                bb.bankAimPending = r.U8() != 0;
                bb.bankTurn.x = r.I16() * 0.0001f;
                bb.bankTurn.y = r.I16() * 0.0001f;
                bb.bankTurn.z = r.I16() * 0.0001f;
                float most = 2.4f * 0.017453293f, turn = M.Length(bb.bankTurn);   // (never more than HIGH's 2.4 degrees)
                if (turn > most) bb.bankTurn *= most / turn;
            }
            g.tun.palmHold = false;   // (THE BALL IN THE LEFT PALM, hosts from 0.3.22; older: the left hand's old point)
            if (r.ok && r.e - r.p >= 1) g.tun.palmHold = (r.U8() & 1) != 0;
            // (four players, hosts from 0.3.24: the balls' spin, bounces, fouls and the host's clock, so BALL LOOK-AHEAD can
            // fly them on; an older host sends none, and the look-ahead stands down there: Game.LookAhead; TASKS.md E48)
            if (np == 4 && !(g.tun.experimental && g.tun.expRoom == Sim.Room.Blitz) && r.ok && r.e - r.p >= 4 * 7 + 4)
            {
                for (int p = 0; p < 4; ++p)
                {
                    Ball b = g.pl[p].ball;
                    b.spin.x = r.I16() / 100f; b.spin.y = r.I16() / 100f; b.spin.z = r.I16() / 100f;
                    byte f = r.U8();
                    b.fouled = (f & 1) != 0; b.homing = (f & 2) != 0; b.bounces = f >> 2;
                }
                float hn = r.F32();
                if (r.ok && float.IsFinite(hn) && hn >= 0f) g.ahead.snapNow = hn;
            }
            // (FIREBALL BLITZ, hosts from 0.3.24: each flying fireball's spin and bounces, then the host's clock; without them,
            // and from an older host, the look-ahead stands down there; TASKS.md E49)
            g.ahead.fireKnown = false;
            if (g.tun.experimental && g.tun.expRoom == Sim.Room.Blitz) g.tun.blitzTeams = false;   // (teams: only as the host says, below)
            if (g.tun.experimental && g.tun.expRoom == Sim.Room.Blitz && r.ok)
            {
                int flying = 0;
                for (int p = 0; p < np; ++p)
                    for (int k = 0; k < Game.KMaxFire; ++k) flying += g.fire[p][k].state == BallState.Flying ? 1 : 0;
                if (r.e - r.p >= 2 * flying + 4)
                {
                    for (int p = 0; p < np; ++p)
                        for (int k = 0; k < Game.KMaxFire; ++k)
                        {
                            Ball f = g.fire[p][k];
                            if (f.state != BallState.Flying) continue;
                            f.spin = new Vec3(0, r.I8() * 0.25f, 0);
                            f.bounces = r.U8();
                        }
                    float hn = r.F32();
                    if (r.ok && float.IsFinite(hn) && hn >= 0f) { g.ahead.snapNow = hn; g.ahead.fireKnown = true; }
                    if (r.ok && r.e - r.p >= 1)
                    {   // (hosts from 0.3.24: the teams it plays)
                        g.tun.teamMode = Math.Min(2, (int)r.U8());
                        g.tun.blitzTeams = true;
                    }
                }
            }
            return r.ok;
        }

        static void ReadEvents(Reader r, ref uint lastId, List<GameEvent> o)
        {
            uint id = r.U32(); byte n = r.U8();
            for (int i = 0; i < n && r.ok; ++i, ++id)
            {
                var e = new GameEvent { type = (EventType)r.U8(), player = r.I8(), hand = r.I8(), pos = r.V3(), value = r.F32() };
                if (r.ok && id > lastId) { o.Add(e); lastId = id; }
            }
        }

        // ---------------------------------------------------------------- host: the room
        Member FindMember(uint id) { foreach (var m in members) if (m.id == id) return m; return null; }
        bool InTank(uint id) { for (int p = 0; p < kWireSlots; ++p) if (id != 0 && slot[p] == id) return true; return false; }
        int Humans() { int n = 0; for (int p = 0; p < kWireSlots; ++p) n += p < np_ && slot[p] != 0 ? 1 : 0; return n; }
        byte WantBots()
        {   // the empty places a bot plays (two players: slot 1 only, as it always was)
            if (np_ == 2) return slot[1] == 0 && botFill ? (byte)2 : (byte)0;
            byte m = 0;   // (four players: any empty corner, once someone plays; the host can move people between corners)
            if (botFill && Humans() != 0) for (int p = 0; p < np_; ++p) if (slot[p] == 0) m |= (byte)(1 << p);
            return m;
        }

        void FreeSpot(Member m)
        {   // the lowest holding-area spot no other waiting member stands on
            // (a room for four: eight spots, four on each side, so a full room with nobody playing has room for all; owner,
            // 2026-11-02)
            int n = np_ == 4 ? 8 : 4;
            var used = new bool[8];
            foreach (var o in members)
                if (o != m && !InTank(o.id)) used[o.spot % n] = true;
            if (m.spot < n && !used[m.spot]) return;
            for (int s = 0; s < n; ++s) if (!used[s]) { m.spot = (byte)s; return; }
        }

        // humans fill slot 0 first; one human plays the bot; any change of who is in the tank restarts the match.
        // Four players keep their corners (empty ones are bots): the host moves people between them (MOVE PLAYERS)
        void CompactTank(Game g, bool reset, bool nextOne = true)   // (nextOne: THE ONE chosen again for a new match)
        {
            np_ = g.tun.players == 4 ? 4 : 2;
            expRoom_ = g.tun.expRoom;
            for (int p = np_; p < kWireSlots; ++p)   // (fewer places: those beyond them go back to waiting)
                if (slot[p] != 0) { Member m = FindMember(slot[p]); if (m != null) FreeSpot(m); slot[p] = 0; }
            if (np_ == 2)
            {   // the people in the first places, in order
                int k = 0;
                for (int p = 0; p < np_; ++p) if (slot[p] != 0) { uint id = slot[p]; slot[p] = 0; slot[k++] = id; }
            }
            foreach (var m in members) if (!InTank(m.id)) FreeSpot(m);
            if (reset)
            {
                if (np_ == 4 && g.tun.teamMode == Teams.ThreeVOne && nextOne)
                {   // (online: the host is THE ONE, or the corner the host picked, or it goes round; a host not playing: it
                    // goes round)
                    int me = LocalSlot();
                    if (g.tun.oneTurns) oneFixed = false;
                    if (!oneFixed) g.tun.theOne = g.tun.oneTurns || me < 0 ? (g.tun.theOne + 1) % 4 : me;
                }
                g.ResetWithBots(WantBots());
                rotateAt = 0;
                eventLog.Clear();
                resumed = false; resumeWait = 0;
            }
            HostRoomInfo();
        }

        // the queue goes into the tank whenever it has a free place (or only the bot): never into a match of two humans
        void FillTank(Game g)
        {
            bool changed = false;
            np_ = g.tun.players == 4 ? 4 : 2;
            expRoom_ = g.tun.expRoom;
            while (queueIds.Count > 0 && Humans() < np_)
            {
                uint id = queueIds[0];
                queueIds.RemoveAt(0);
                if (FindMember(id) == null) continue;
                for (int p = 0; p < np_; ++p) if (slot[p] == 0) { slot[p] = id; break; }
                changed = true;
                Log("net: queue -> tank");
            }
            if (changed) { CompactTank(g, true); Notice("NEXT PLAYERS IN THE TANK"); }
        }

        // match over: the players leave the tank (to the holding area), the queue goes in
        void Rotate(Game g)
        {
            Log($"net: rotation ({queueIds.Count} queued)");
            Array.Clear(slot, 0, slot.Length);
            CompactTank(g, false);
            FillTank(g);
            if (Humans() == 0) CompactTank(g, true);
        }

        void RemoveMember(uint id, Game g)
        {
            Member it = null;
            foreach (var m in members) if (m.id == id && !m.local) it = m;
            if (it == null) return;
            kickVotes.RemoveAll(v => v.target == id || v.voter == id);
            Log($"net: {IpStr(it.ip)} left the room");
            members.Remove(it);
            queueIds.RemoveAll(q => q == id);
            bool wasPlaying = InTank(id);
            for (int p = 0; p < kWireSlots; ++p) if (slot[p] == id) slot[p] = 0;
            if (wasPlaying) { CompactTank(g, true); FillTank(g); Notice("PLAYER LEFT"); }
            else HostRoomInfo();
        }

        void HostRoomInfo()
        {
            for (int p = 0; p < kWireSlots; ++p) room.slot[p] = slot[p];
            room.botMask = (np_ == 4 ? Humans() > 0 : slot[0] != 0) ? WantBots() : (byte)0;
            room.bot = room.IsBot(1);
            room.members.Clear();
            foreach (var m in members)
            {
                if (InTank(m.id)) continue;
                var rm = new RoomInfo.Member { id = m.id, role = RoomInfo.Waiting, spot = m.spot, @in = m.@in.Clone() };
                int q = queueIds.IndexOf(m.id);
                rm.queuePos = (sbyte)q;
                room.members.Add(rm);
            }
            room.watchers.Clear();
            foreach (var sp in spectators)
                if (sp.where <= 1) room.watchers.Add(new RoomInfo.Watcher { id = sp.id, where = sp.where, pos = sp.pos });
            room.names.Clear();
            foreach (var m in members)
            {
                string n = m.local ? name : m.name;
                if (n.Length > 0) room.names.Add(new RoomInfo.Name { id = m.id, name = n });
            }
            foreach (var sp in spectators)
                if (sp.where == 1 && sp.name.Length > 0) room.names.Add(new RoomInfo.Name { id = sp.id, name = sp.name });
        }

        // that room's name (its host's name; empty: none)
        public string HereName()
        {
            if (mode == Mode.Host) return roomName;
            uint ip = mode == Mode.Client ? peerIp : (mode == Mode.Spectate && presWhere == 1) ? presWatch : 0;
            foreach (var lg in games) if (ip != 0 && lg.ip == ip) return lg.name;
            return "";
        }

        // MUTED PLAYERS (TASKS.md E46, native 43397af, 0.3.23): the community server's code for a person (by session id, as
        // Person.id and a voice frame's speaker), made for this player only (MUTECODE: an HMAC of the two players' key
        // hashes): the same person keeps it under a new name and address, and nobody else's game gets the same one. 0: not
        // known (yet), at home (no server), an older server, or either player without a statistics key. AskMuteCodes asks
        // the server for the ones not known (each at most every 5 s, at most 16 a request)
        readonly Dictionary<uint, uint> addrOf = new Dictionary<uint, uint>();      // online: each session's virtual address (from its packets)
        readonly Dictionary<uint, ulong> muteCodes = new Dictionary<uint, ulong>(); // MUTECODE's answers, by virtual address
        readonly Dictionary<uint, double> muteAsked = new Dictionary<uint, double>();
        public ulong MuteCode(uint sess) => addrOf.TryGetValue(sess, out uint va) && muteCodes.TryGetValue(va, out ulong c) ? c : 0;
        // (guests and spectators: whose virtual address is whose, as the host's ROOM says; 0.3.24, native 3fccaf9)
        void LearnAddrs(RoomInfo ri)
        {
            if (!online) return;
            foreach (RoomInfo.Addr x in ri.addrs)
            {
                if (x.id == session) continue;
                if (addrOf.Count > 512) addrOf.Clear();
                addrOf[x.id] = x.vip;
            }
        }
        public void AskMuteCodes(IEnumerable<uint> sessions)
        {
            if (!online || vip == 0) return;
            var body = new List<byte>();
            int n = 0;
            foreach (uint s in sessions)
            {
                if (!addrOf.TryGetValue(s, out uint va) || muteCodes.ContainsKey(va) || n >= 16) continue;
                if (muteAsked.TryGetValue(va, out double at) && at > 0 && now - at < 5.0) continue;
                muteAsked[va] = now;
                body.AddRange(BitConverter.GetBytes(va));
                ++n;
            }
            if (n > 0) RelaySend(R_MUTECODE, body.ToArray());
        }

        // REPORT / MUTE A PLAYER: everyone else in our room, or at the arena a spectator stands at (role: VOICE_*)
        public List<Person> PeopleHere()
        {
            var v = new List<Person>();
            RoomInfo ri = InRoom ? room : null;
            if (mode == Mode.Spectate && presWhere == 1)
                foreach (var m in matches) if (m.ip == presWatch) ri = m.room;
            if (ri == null) return v;
            void Add(uint id, byte role)
            {
                if (id == 0 || id == session) return;
                foreach (var p in v) if (p.id == id) return;
                string n = ri.NameOf(id) ?? $"PLAYER {id & 0xffff:X4}";   // a build without names
                v.Add(new Person { id = id, role = role, name = n });
            }
            for (int s = 0; s < kWireSlots; ++s) Add(ri.slot[s], VOICE_PLAYER);   // (all four corners)
            foreach (var m in ri.members) Add(m.id, VOICE_WAITING);
            foreach (var w in ri.watchers) if (w.where == 1) Add(w.id, VOICE_SPECTATOR);
            return v;
        }

        void SendRoom()
        {
            var w = new Writer(); Header(w, MSG_ROOM, session, 0);
            WriteRoom(w, room, ++roomSeqCounter);
            if (online)
            {   // (0.3.24, MUTED PLAYERS: each person's virtual address, so a guest can ask the server for their code; as many
                // as fit the relay's 1200 bytes. Never at home: there they would be the network's own addresses)
                var v = new List<RoomInfo.Addr>();
                foreach (var m in members) if (!m.local && m.id != 0 && m.ip != 0) v.Add(new RoomInfo.Addr { id = m.id, vip = m.ip });
                foreach (var sp in spectators) if (sp.where == 1 && sp.id != 0 && sp.ip != 0) v.Add(new RoomInfo.Addr { id = sp.id, vip = sp.ip });
                int roomLeft = w.b.Count + 1 < 1200 ? (1200 - w.b.Count - 1) / 8 : 0;
                int n = Math.Min(Math.Min(v.Count, 16), roomLeft);
                if (roomLeft > 0)
                {
                    w.U8((byte)n);
                    for (int i = 0; i < n; ++i) { w.U32(v[i].id); w.U32(v[i].vip); }
                }
            }
            byte[] d = w.ToArray();
            foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
            foreach (var sp in spectators) SendTo(d, sp.ip, kPort);
        }

        // MOVE PLAYERS (four players, owner 2026-10-30): the host swaps two corners (people or bots; their teams go with
        // the corner, THE ONE with the player) or makes a corner THE ONE (3 v 1). Either restarts the match
        public bool HostSwap(int a, int b, Game g)
        {
            if (mode != Mode.Host || np_ != 4 || a < 0 || b < 0 || a >= np_ || b >= np_ || a == b) return false;
            (slot[a], slot[b]) = (slot[b], slot[a]);
            if (g.tun.theOne == a) g.tun.theOne = b;   // (THE ONE goes with the player)
            else if (g.tun.theOne == b) g.tun.theOne = a;
            CompactTank(g, true, false);
            Notice("PLAYERS MOVED");
            Log($"net: the host swapped corners {a} and {b}");
            return true;
        }

        public bool HostMakeOne(int corner, Game g)
        {
            if (mode != Mode.Host || np_ != 4 || g.tun.teamMode != Teams.ThreeVOne || corner < 0 || corner >= np_) return false;
            g.tun.theOne = corner;
            oneFixed = true;
            CompactTank(g, true, false);
            Notice("A NEW ONE");
            Log($"net: the host made corner {corner} THE ONE");
            return true;
        }

        // ---------------------------------------------------------------- host handoff
        // HOST HANDOFF (native 0.3.13, owner 2026-11-02: one headset going to sleep ended everyone's room). The host tells
        // the room who comes next (HEIRS, once a second: the people in it, longest there first, and their addresses). When
        // the host goes quiet for 5 s, or hands the room over (MAKE HOST, or LEAVE GAME with people still in it), the next
        // one hosts it from the latest snapshot and everyone else follows; the room keeps its name and rules. A match in
        // progress pauses and picks up where it was (balls back to their throwers, a 3 s countdown); if one of its players
        // is gone, it starts again, as whenever a player leaves. A host that wakes up to find its room moved on rejoins it.

        byte HeirFlagsNow() => (byte)((botFill ? 1 : 0) | (privateTalk ? 2 : 0) | (oneTurns ? 4 : 0) | (oneFixed ? 8 : 0));
        static Heir HeirOf(uint id, uint ip, string name) => new Heir { id = id, ip = ip, name = name ?? "" };

        // host: who hosts next, to everyone in the room (`first` first, then the longest there), with the room's settings
        // and the people it has removed (they stay out under the next host)
        void SendHeirs(uint first = 0)
        {
            lastHeirs = now;
            var w = new Writer(); Header(w, MSG_HEIRS, session, 0);
            w.U8(HeirFlagsNow());
            WriteName(w, roomName);
            var order = new List<Member>();
            Member f = FindMember(first);
            if (f != null && !f.local) order.Add(f);
            foreach (var m in members) if (!m.local && m.id != first) order.Add(m);
            int n = Math.Min(order.Count, 12);
            w.U8((byte)n);
            for (int i = 0; i < n; ++i) { w.U32(order[i].id); w.U32(order[i].ip); WriteName(w, order[i].name); }
            int k = Math.Min(removed.Count, 16);   // (the latest 16)
            w.U8((byte)k);
            for (int i = removed.Count - k; i < removed.Count; ++i) { w.U32(removed[i].id); w.U32(removed[i].ip); WriteName(w, removed[i].name); }
            byte[] d = w.ToArray();
            foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
        }

        // host: the room goes to `target` (someone in it). HEIRS with them first, then HANDOFF, three times over (should
        // every copy be lost, the room still finds them once we go quiet: they head the list)
        void HandOver(uint target, bool stay)
        {
            SendHeirs(target);
            var w = new Writer(); Header(w, MSG_HANDOFF, session, 0); w.U32(target); w.Bool(stay);
            byte[] d = w.ToArray();
            for (int k = 0; k < 3; ++k)
                foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
            Member t = FindMember(target);
            Log($"net: the room goes to {(t != null && t.name.Length > 0 ? t.name : "the next player")}{(stay ? " (we stay in it)" : "")}");
        }

        // host: no longer hosting (handed over, left, or woken to find the room moved on): the room's state goes, and our
        // own ROOM TALK and BOT FILLS IN come back if the room was one we took over
        void StepDown()
        {
            if (inherited) { privateTalk = ownTalk; botFill = ownBotFill; inherited = false; }
            members.Clear(); queueIds.Clear(); spectators.Clear(); eventLog.Clear(); kickVotes.Clear();
            Array.Clear(slot, 0, slot.Length);
            resumeWait = 0; prevHost = 0; promotedAt = 0;
            pingAvg = -1;   // (a new host: its round trip and its clock measured afresh: BALL LOOK-AHEAD)
            hostOffSet = false;
        }

        void SendJoin(bool play)
        {
            lastJoin = now;
            var j = new Writer(); Header(j, MSG_JOIN, session, ++seq); j.Bool(play); WriteName(j, name);
            // (native 0.3.20: our version after the name, FAIR MATCHES; older hosts stop reading at the name)
            j.U8((byte)app.Length);
            foreach (char c in app) j.U8((byte)c);
            SendTo(j, peerIp);
        }

        // client: out of the room (the host closed it, turned us away, or nobody took it over)
        void DropRoom(string why)
        {
            if (why != null) Notice(why);
            mode = Mode.Offline; peerIp = 0; peerSeen = 0; peerSession = 0; room = new RoomInfo();
            migrating = false; scoutUntil = 0; heirs.Clear(); heirRemoved.Clear();
        }

        // that heir hosts a room we heard since then (a room handed over: not one of their own)
        bool HeirBeacon(uint id, uint ip, double since)
        {
            foreach (var lg in games) if (lg.session == id && lg.ip == ip && lg.seen > since && lg.continues != 0) return true;
            return false;
        }

        // the host makes someone in the room the host; it stays, waiting (MAKE HOST)
        public bool HostHandOff(uint target, Game g)
        {
            Member t = FindMember(target);
            if (mode != Mode.Host || t == null || t.local) return false;
            {   // the latest snapshot to everyone first (the people waiting get 30 a second): the new host goes on from here
                var w = new Writer(); Header(w, MSG_STATE, session, ++seq); WriteEvents(w, eventLog); WriteState(w, g);
                byte[] d = w.ToArray();
                foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
            }
            oneTurns = g.tun.oneTurns;
            HandOver(target, true);
            var next = new List<Heir>();   // our own list, should the new host not answer
            foreach (var m in members)
            {
                if (m.local) continue;
                Heir h = HeirOf(m.id, m.ip, m.name);
                if (m.id == target) next.Insert(0, h); else next.Add(h);
            }
            heirFlags = HeirFlagsNow();
            heirRoom = roomName;
            heirRemoved.Clear();
            foreach (var x in removed) heirRemoved.Add(HeirOf(x.id, x.ip, x.name));
            StepDown();
            mode = Mode.Client;   // (room stays as we had it until the new host's comes)
            prevHostId = session;
            heirs = next; heirAt = 1;
            Follow(next[0]);
            Notice($"{(next[0].name.Length > 0 ? next[0].name : "A PLAYER")} IS NOW THE HOST");
            return true;
        }

        // client: the room's host is now h: everything goes to them (JOIN until they list us; the room as we had it till then)
        void Follow(Heir h)
        {
            peerIp = h.ip; peerSession = h.id; peerSeen = now;
            lastRecvSeq = 0; lastRoomSeq = 0; lastEventId = 0; pendingEvents.Clear();
            if (!migrating) migrateFrom = now;
            migrating = true; migrateUntil = now + kHeirWait; scoutUntil = 0;
            SendJoin(false);
            Log($"net: following the room to {(h.name.Length > 0 ? h.name : IpStr(h.ip))}");
        }

        void Arrived()
        {   // client: the new host answers: the room is theirs
            migrating = false;
            string who = null;
            foreach (var h in heirs) if (h.id == peerSession && h.name.Length > 0) who = h.name;
            Notice($"{who ?? "A PLAYER"} IS NOW THE HOST");
            Log($"net: {who ?? IpStr(peerIp)} hosts the room now");
        }

        // client: the next in line takes the room (ourselves: we host it); nobody left: the room is gone
        void NextHeir(Game g)
        {
            while (heirAt < heirs.Count)
            {
                Heir h = heirs[heirAt++];
                if (h.id == prevHostId) continue;
                if (h.id == session) { Promote(g, false, prevHost, prevHostId); return; }
                Follow(h);
                return;
            }
            Log("net: host lost, and nobody took the room over");
            DropRoom("CONNECTION TO HOST LOST");
        }

        // client -> host of the room we are in: its people (the old host too if it stays), their places in the tank, the
        // queue and the holding area as the last ROOM had them, its settings from HEIRS, and the game from the last snapshot
        void Promote(Game g, bool oldStays, uint oldIp, uint oldId)
        {
            RoomInfo was = room;
            var heirList = new List<Heir>(heirs);
            mode = Mode.Host;
            if (!inherited) { ownTalk = privateTalk; ownBotFill = botFill; }
            inherited = true;
            botFill = (heirFlags & 1) != 0; privateTalk = (heirFlags & 2) != 0; oneFixed = (heirFlags & 8) != 0;
            g.tun.oneTurns = oneTurns = (heirFlags & 4) != 0;
            roomName = heirRoom.Length > 0 ? heirRoom : name;
            prevHost = oldIp; prevHostId = oldId; promotedAt = now;
            members.Clear(); queueIds.Clear(); spectators.Clear(); kickVotes.Clear(); eventLog.Clear();
            foreach (var h in heirRemoved)
            {   // (whom the room's hosts removed stay out)
                bool known = false;
                foreach (var x in removed) known |= x.id == h.id;
                if (!known) removed.Add(new Removed { id = h.id, ip = h.ip, name = h.name });
            }
            RoomInfo.Member Was(uint id) { foreach (var rm in was.members) if (rm.id == id) return rm; return null; }
            var me = new Member { id = session, local = true, seen = now, active = now };
            RoomInfo.Member mine = Was(session);
            if (mine != null) me.spot = mine.spot;
            members.Add(me);
            void Add(uint id, uint ip, string nm)
            {
                if (id == 0 || ip == 0 || id == session || FindMember(id) != null) return;
                var m = new Member { id = id, ip = ip, active = now, back = false };
                RoomInfo.Member rm = Was(id);
                if (rm != null) { m.spot = rm.spot; m.@in = rm.@in.Clone(); }   // (where they stood, till they're back)
                for (int p = 0; p < kWireSlots; ++p) if (was.slot[p] == id && p < g.np) m.@in = g.pl[p].@in.Clone();
                m.seen = now + kResumeWait - kMemberTimeout;   // (time to find us: they go when that runs out)
                if (nm != null) m.name = nm;
                members.Add(m);
            }
            foreach (var h in heirList) if (h.id != oldId) Add(h.id, h.ip, h.name);
            if (oldStays) Add(oldId, oldIp, was.NameOf(oldId));
            bool lost = false;
            for (int p = 0; p < kWireSlots; ++p)
            {
                slot[p] = was.slot[p] != 0 && FindMember(was.slot[p]) != null ? was.slot[p] : 0;
                lost |= was.slot[p] != 0 && slot[p] == 0;
            }
            var q = new List<RoomInfo.Member>();
            foreach (var rm in was.members) if (rm.queuePos >= 0 && FindMember(rm.id) != null && !InTank(rm.id)) q.Add(rm);
            // (stable, as native's std::stable_sort: equal places keep the room's order)
            var ordered = new List<RoomInfo.Member>(q);
            ordered.Sort((a, b) => a.queuePos != b.queuePos ? a.queuePos.CompareTo(b.queuePos) : q.IndexOf(a).CompareTo(q.IndexOf(b)));
            foreach (var rm in ordered) queueIds.Add(rm.id);
            np_ = g.np == 4 ? 4 : 2; expRoom_ = g.tun.expRoom;
            heirs.Clear(); heirRemoved.Clear(); migrating = false; scoutUntil = 0;
            peerIp = 0; peerSession = 0; peerSeen = now;
            lastBeacon = 0; lastRoomSent = 0; lastHeirs = 0; rotateAt = 0; lastClosingWarn = 0;
            if (lost)
            {   // a player in the match is gone: it starts again, as when anyone leaves
                CompactTank(g, true);
                resumeWait = 0;
                Notice("YOU ARE NOW THE HOST - A NEW MATCH");
            }
            else
            {
                g.ResumeAfterHandoff();
                resumed = true;
                resumeWait = Humans() != 0 ? now + kResumeWait : 0;
                HostRoomInfo();
                Notice("YOU ARE NOW THE HOST");
            }
            Log($"net: we host the room now ({members.Count} people, {(lost ? "a new match" : "the match goes on")})");
        }

        // g's STATE read into another game, as a spectator's (tests); cut: bytes left off its end (an older host's snapshot)
        public static bool SnapshotInto(Game g, Game into, int cut = 0)
        {
            var w = new Writer();
            WriteState(w, g);
            byte[] b = w.b.ToArray();
            return ReadState(new Reader(b, 0, b.Length - Math.Min(cut, b.Length)), into, -1);
        }

        // a STATE packet's size with a full event log (tests: the relay's 1200 bytes)
        public static int SnapshotBytes(Game g)
        {
            var w = new Writer(); Header(w, MSG_STATE, 0, 0);
            var log = new LinkedList<SentEvent>();
            for (int i = 0; i < 24; ++i) log.AddLast(new SentEvent { id = (uint)i + 1 });
            WriteEvents(w, log);
            WriteState(w, g);
            return w.b.Count;
        }

        // host: how many votes remove someone now (0 = too few people to vote)
        public int KickVotesNeeded()
        {
            int people = members.Count;   // everyone in the room, the host included
            int others = people - 1;      // all but the one being voted on
            int need = Math.Max(2, others / 2 + 1);
            return need <= others ? need : 0;
        }

        void CountKickVote(uint target, uint voter)
        {
            if (mode != Mode.Host || target == session || target == voter) return;   // (the host can't be removed)
            if (FindMember(target) == null || FindMember(voter) == null) return;
            foreach (var v in kickVotes) if (v.target == target && v.voter == voter) return;
            kickVotes.Add((target, voter));
            int n = 0;
            foreach (var v in kickVotes) if (v.target == target && FindMember(v.voter) != null) ++n;
            int need = KickVotesNeeded();
            Member t = FindMember(target);
            Log($"net: a vote to remove {(t.name.Length > 0 ? t.name : IpStr(t.ip))} ({n} of {need} needed)");
            if (need != 0 && n >= need) kickNow = target;
            else Notice($"VOTE TO REMOVE {(t.name.Length > 0 ? t.name : "A PLAYER")}  {n} OF {(need != 0 ? need : 2)}");
        }

        // the host removes someone from its arena at once (0.3.8; owner's decision)
        public bool HostKick(uint target)
        {
            if (mode != Mode.Host || target == 0 || target == session || FindMember(target) == null) return false;
            kickNow = target;
            kickByHost = true;
            return true;
        }

        // VOTE TO REMOVE: false when not allowed (not in a room, the host, yourself, or already voted)
        public bool VoteKick(uint target)
        {
            if (!InRoom || target == 0 || target == session || target == HostSession) return false;
            if (myKickVotes.Contains(target)) return false;
            myKickVotes.Add(target);
            if (mode == Mode.Host) { CountKickVote(target, session); return true; }
            var w = new Writer(); Header(w, MSG_KICKVOTE, session, 0); w.U32(target);
            SendTo(w, peerIp);
            Log($"net: voted to remove {target:x8}");
            return true;
        }

        void Reject(uint ip, string why)
        {
            var w = new Writer(); Header(w, MSG_REJECT, session, 0);
            for (int i = 0; i < 32; ++i) w.U8(i < why.Length && i < 31 ? (byte)why[i] : (byte)0);
            SendTo(w, ip);
        }

        // ---------------------------------------------------------------- voice
        // body: speaker id (u32, set by the host), role (u8), kVoiceSamples mu-law bytes
        public bool VoiceZone => InRoom || (mode == Mode.Spectate && presWhere == 1 && presWatch != 0);

        public void SendVoice(byte[] ulaw)
        {
            if (mode == Mode.Host)
            {   // we talk: straight to everyone in our room
                byte role = InTank(session) ? VOICE_PLAYER : VOICE_WAITING;
                ForwardVoice(session, role, ulaw, 0, session);
                return;
            }
            uint to = mode == Mode.Client ? peerIp : (mode == Mode.Spectate && presWhere == 1) ? presWatch : 0;
            if (to == 0) return;
            var w = new Writer(); Header(w, MSG_VOICE, session, 0);
            w.U32(0); w.U8(0);
            w.Bytes(ulaw, 0, kVoiceSamples);
            SendTo(w, to);
        }

        void ForwardVoice(uint speaker, byte role, byte[] ulaw, uint exceptIp, uint exceptId)
        {
            var w = new Writer(); Header(w, MSG_VOICE, session, 0);
            w.U32(speaker); w.U8(role);
            w.Bytes(ulaw, 0, kVoiceSamples);
            byte[] d = w.ToArray();
            foreach (var m in members)
                if (!m.local && !(m.ip == exceptIp && m.id == exceptId)) SendTo(d, m.ip, kPort);
            foreach (var sp in spectators)   // only spectators standing at this arena hear it (and not when the room's talk is private)
                if (!privateTalk && sp.where == 1 && !(sp.ip == exceptIp && sp.id == exceptId)) SendTo(d, sp.ip, kPort);
            if (speaker != session && voiceIn.Count < 64)
            {   // the host hears it too
                var f = new VoiceFrame { speaker = speaker, role = role };
                Array.Copy(ulaw, f.data, kVoiceSamples);
                voiceIn.Add(f);
            }
        }

        // ---------------------------------------------------------------- team cameras
        // body: original sender (u32), team (16), blob length (u16), blob
        public void ShareCameras(string team, byte[] blob)
        {
            if (blob.Length > 400) return;
            var w = new Writer(); Header(w, MSG_CAMERAS, session, 0);
            w.U32(session);
            for (int i = 0; i < 16; ++i) w.U8(i < team.Length && i < 15 ? (byte)team[i] : (byte)0);
            w.U16((ushort)blob.Length);
            w.Bytes(blob, 0, blob.Length);
            byte[] d = w.ToArray();
            foreach (var lg in games) if (now - lg.seen < 2.5) SendTo(d, lg.ip, kPort);
        }

        public bool TakeCameras(out string team, out byte[] blob)
        {
            team = camTeam; blob = camBlob;
            if (!camFresh) return false;
            camFresh = false;
            return camFrom != session;
        }

        // ---------------------------------------------------------------- per-frame
        // Once per frame. `in` receives the two tank players' inputs to step the Game with (host / offline: in[0] is the
        // local player when we are in slot 0). Client: applies the host's snapshot to `g` and sends `local`. Returns
        // true when the caller should run Game.Step (offline, or hosting with someone in the tank).
        public bool Exchange(PlayerInput local, PlayerInput[] @in, Game g, float dt)
        {
            if (kickNow != 0 && mode == Mode.Host)
            {   // VOTE TO REMOVE passed: out of the room, and not back this session
                Member m = FindMember(kickNow);
                if (m != null)
                {
                    removed.Add(new Removed { id = m.id, ip = m.ip, name = m.name });
                    string msg = $"{(m.name.Length > 0 ? m.name : "A PLAYER")} WAS REMOVED{(kickByHost ? "" : " BY VOTE")}";
                    Log($"net: {(m.name.Length > 0 ? m.name : IpStr(m.ip))} removed by {(kickByHost ? "the host" : "vote")}");
                    Reject(m.ip, kickByHost ? "REMOVED BY THE HOST" : "REMOVED BY VOTE");
                    RemoveMember(kickNow, g);
                    Notice(msg);
                }
                kickNow = 0;
                kickByHost = false;
            }
            now = MonoNow();
            double gap = lastExchange > 0 ? now - lastExchange : 0;   // (a headset asleep: no frames)
            lastExchange = now;
            if (online && vip != 0 && gap > 8.0)
            {   // asleep that long: the relay has let our session go (10 s). A new one
                Log($"net: no frames for {gap:F0} s: a new session on the relay");
                vip = 0;
                RelayHello();
            }
            if (gap > kHostQuiet && mode == Mode.Host)
            {   // we slept: our room has another host by now. Rejoin it as a player
                var next = new List<Heir>();
                foreach (var m in members) if (!m.local) next.Add(HeirOf(m.id, m.ip, m.name));
                if (next.Count > 0)
                {
                    // (which of them hosts it now, the beacons say: listen a few seconds rather than knock on every door;
                    // nobody hosting one: it has closed)
                    Log($"net: no frames for {gap:F0} s while hosting: the room went on without us; looking for it");
                    StepDown();
                    mode = Mode.Client; peerIp = 0; peerSession = 0; prevHostId = session;
                    room = new RoomInfo();   // (we come back to the waiting area)
                    heirs = next; heirAt = 0;
                    migrating = true; migrateFrom = now; scoutUntil = now + 4.0;
                    Notice("YOU WERE AWAY - REJOINING THE ROOM");
                }
            }
            else if (gap > 3.0 && mode == Mode.Client && !migrating)
            {   // we slept: the host may have let us go. Ask again
                Log($"net: no frames for {gap:F0} s: asking the host to let us back in");
                peerSeen = now; lastJoin = 0;
                room = new RoomInfo();
                wokeAt = now;
            }
            bool gotState = false;
            EndPoint any = new IPEndPoint(IPAddress.Any, 0);
            for (;;)
            {
                int n;
                IPEndPoint from;
                try
                {
                    if (sock == null || sock.Available <= 0) break;
                    EndPoint ep = any;
                    n = sock.ReceiveFrom(buf, ref ep);
                    from = (IPEndPoint)ep;
                }
                catch (SocketException ex)
                {
                    if (ex.SocketErrorCode == SocketError.WouldBlock) break;
                    continue;   // a stray ICMP error: keep reading
                }
                if (n <= 0) break;
                if (now < deafUntil) continue;   // (Deafen: tests)
                int start = 0;
                uint fromIp = IpOf(from.Address);
                if (online)
                {   // only the relay talks to us; unwrap DELIVER and treat the sender's virtual address as its IP
                    if (fromIp != serverIp || from.Port != serverPort) continue;
                    var rr = new Reader(buf, 0, n);
                    uint mg = rr.U32(); byte t = rr.U8();
                    if (!rr.ok || mg != kRelayMagic) continue;
                    lastRelayHeard = now;
                    if (t == R_WELCOME)
                    {
                        if (rr.Left < 20) continue;
                        Array.Copy(buf, rr.p, token, 0, 16); vip = BitConverter.ToUInt32(buf, rr.p + 16);
                        keySent = false;   // (a new session on the relay: name our stats key again)
                        addrOf.Clear(); muteCodes.Clear(); muteAsked.Clear();   // (MUTED PLAYERS: new addresses)
                        Log($"net: online (relay {serverName})");
                        if (rr.Left >= 21)
                        {   // the current release (relays from 2026-10-19 on)
                            int len = Math.Min(15, Math.Min((int)buf[rr.p + 20], rr.Left - 21));
                            var sb = new StringBuilder();
                            for (int i = 0; i < len; ++i)
                            {
                                char c = (char)buf[rr.p + 21 + i];
                                if ((c >= '0' && c <= '9') || c == '.') sb.Append(c); else break;
                            }
                            latest = sb.ToString();
                        }
                        continue;
                    }
                    if (t == R_BOARD)
                    {   // a TRAINING leaderboard
                        if (rr.Left < 3) continue;
                        var bd = new Board { lesson = buf[rr.p], kind = buf[rr.p + 1] };
                        int rows = Math.Min((int)buf[rr.p + 2], 20);
                        if (rr.Left < 3 + 20 * rows + 8) continue;
                        for (int i = 0; i < rows; ++i)
                        {
                            int q0 = rr.p + 3 + 20 * i;
                            var sb = new StringBuilder();
                            for (int k = 0; k < 16; ++k) { byte ch = buf[q0 + k]; if (ch >= 32 && ch < 127) sb.Append((char)ch); else break; }
                            bd.rows.Add(new BoardRow { name = sb.ToString(), value = BitConverter.ToUInt32(buf, q0 + 16) });
                        }
                        int q = rr.p + 3 + 20 * rows;
                        bd.myPlace = BitConverter.ToUInt16(buf, q); bd.myValue = BitConverter.ToUInt32(buf, q + 2); bd.players = BitConverter.ToUInt16(buf, q + 6);
                        bd.at = now;
                        boards.RemoveAll(x => x.lesson == bd.lesson && x.kind == bd.kind);
                        boards.Add(bd);
                        continue;
                    }
                    if (t == R_STATS)
                    {   // a host's two players' careers
                        if (rr.Left < 4 + 2 * 51) continue;
                        statsHost = BitConverter.ToUInt32(buf, rr.p);
                        for (int k = 0; k < 2; ++k)
                        {
                            int q = rr.p + 4 + 51 * k;
                            var c = new CareerStats { known = buf[q] != 0 };
                            var nm = new StringBuilder();
                            for (int i = 0; i < 16; ++i) { byte ch = buf[q + 1 + i]; if (ch >= 32 && ch < 127) nm.Append((char)ch); else break; }
                            c.name = nm.ToString();
                            uint V(int i) => BitConverter.ToUInt32(buf, q + 17 + 4 * i);
                            c.matches = V(0); c.wins = V(1); c.throws = V(2); c.onTarget = V(3);
                            c.hits = V(4); c.blocks = V(5); c.faced = V(6); c.catches = V(7);
                            c.topSpeed = BitConverter.ToUInt16(buf, q + 49) / 100f;
                            stats[k] = c;
                        }
                        continue;
                    }
                    if (t == R_CROWD)
                    {   // our fans (we host an online match): keep them, and pass them on to the room
                        if (mode != Mode.Host || !ReadCrowd(buf, rr.p, rr.e, crowd, now)) continue;
                        var w = new Writer(); Header(w, MSG_CROWD, session, 0);
                        w.Bytes(buf, rr.p, 12);
                        byte[] d = w.ToArray();
                        foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
                        foreach (var sp in spectators) SendTo(d, sp.ip, kPort);
                        continue;
                    }
                    if (t == R_MUTECODE)
                    {   // MUTED PLAYERS: (virtual address, its code for us) pairs; zeros: none
                        for (int q = rr.p; q + 12 <= rr.e; q += 12)
                        {
                            uint va = BitConverter.ToUInt32(buf, q);
                            ulong code = BitConverter.ToUInt64(buf, q + 4);
                            if (code != 0) muteCodes[va] = code;
                        }
                        continue;
                    }
                    if (t == R_ERROR)
                    {
                        var msg = new char[48];
                        int len = Math.Min(48, rr.Left);
                        for (int i = 0; i < len; ++i) { char c = (char)buf[rr.p + i]; msg[i] = c != 0 && (c < 32 || c > 'Z') ? ' ' : c; }
                        string s = CStr(msg);
                        Notice(s);
                        Log($"net: relay says {s}");
                        continue;
                    }
                    if (t != R_DELIVER) continue;
                    uint src = rr.U32();
                    if (!rr.ok) continue;
                    fromIp = src;
                    start = rr.p;
                }
                if (Handle(buf, start, n, fromIp, g)) gotState = true;
            }
            if (online)
            {   // keep the relay session alive; say hello again until welcomed
                if (vip == 0 && now - lastHello > 1.0) RelayHello();
                else if (vip != 0 && now - lastRelayPing > 2.0)
                {
                    lastRelayPing = now;
                    var b = new byte[21];
                    BitConverter.GetBytes(kRelayMagic).CopyTo(b, 0); b[4] = R_PING; Array.Copy(token, 0, b, 5, 16);
                    RawSend(b, serverEp);
                }
                if (now - lastRelayHeard > 12.0 && vip != 0) { vip = 0; Notice("SERVER NOT RESPONDING"); Log("net: relay silent"); }
                bool anyKey = false;
                foreach (byte k in statsKey) anyKey |= k != 0;
                if (vip != 0 && anyKey && !keySent) { RelaySend(R_KEY, (byte[])statsKey.Clone()); keySent = true; }
                for (int k = 0; k < 8 && vip != 0 && keySent && soundQ.Count > 0; ++k)
                {   // (SOUND STUDIO's votes, a few at a time)
                    string f = null; int v = 0;
                    foreach (var kv in soundQ) { f = kv.Key; v = kv.Value; break; }
                    byte[] fn = Encoding.ASCII.GetBytes(f);
                    var b = new byte[fn.Length + 2];
                    b[0] = (byte)fn.Length;
                    Array.Copy(fn, 0, b, 1, fn.Length);
                    b[1 + fn.Length] = (byte)(sbyte)v;
                    RelaySend(R_SOUNDVOTE, b);
                    Log($"sound studio: vote {v:+0;-0;0} for {f} sent");
                    soundQ.Remove(f);
                }
                for (int k = 0; k < 4 && vip != 0 && keySent && trainQ.Count > 0; ++k)
                {   // (TRAINING results, a few at a time)
                    RelaySend(R_TRAIN, trainQ[0]);
                    Log($"stats: training result sent (lesson {trainQ[0][0]})");
                    trainQ.RemoveAt(0);
                }
                if (vip != 0 && mode == Mode.Host && now - lastTank > 3.0)
                {   // who is in the tank (stats go to them)
                    lastTank = now;
                    var v = new uint[2];
                    for (int k = 0; k < 2 && np_ == 2; ++k)
                    {   // (four players: no careers, they compare one-on-one matches)
                        if (slot[k] == 0) continue;
                        foreach (var m in members) if (m.id == slot[k]) v[k] = m.local ? vip : m.ip;
                    }
                    var b = new byte[8];
                    BitConverter.GetBytes(v[0]).CopyTo(b, 0); BitConverter.GetBytes(v[1]).CopyTo(b, 4);
                    RelaySend(R_TANK, b);
                }
                if (vip != 0 && mode == Mode.Host && liveSet && now - lastLive > 1.0)
                {   // The Crowd: our match, for the fans
                    lastLive = now;
                    // (four players, 0.3.23: 104 bytes more after the 82 every relay reads, which older relays leave: a 4, the
                    // room, its team mode and THE ONE; for each of the four its colour (3), team, points (i16), health and out;
                    // the four countries; players 2 and 3's names (16) and match counts (8 x u16). Players 0 and 1's names and
                    // counts are the 82's)
                    var b = new byte[live.np == 4 ? 82 + 104 : 82];
                    b[0] = live.state;
                    Array.Copy(live.score, 0, b, 1, 4);
                    b[5] = live.nation[0]; b[6] = live.nation[1];
                    for (int k = 0; k < 2; ++k) for (int c = 0; c < 3; ++c) b[7 + 3 * k + c] = live.rgb[k, c];
                    b[13] = live.flags;
                    ushort cl = (ushort)Math.Min(65535f, Math.Max(0f, live.clock * 10f));
                    BitConverter.GetBytes(cl).CopyTo(b, 14);
                    for (int k = 0; k < 2; ++k)
                        for (int i = 0; i < 16 && i < live.name[k].Length; ++i) b[16 + 16 * k + i] = (byte)live.name[k][i];
                    for (int k = 0; k < 2; ++k)
                    {   // this match so far
                        MatchTally s = live.st[k];
                        ushort[] v = { s.throws, s.onTarget, s.hits, s.blocks, s.faced, s.catches, s.strikes,
                                       (ushort)Math.Min(6000f, Math.Max(0f, s.topSpeed * 100f)) };
                        for (int i = 0; i < 8; ++i) BitConverter.GetBytes(v[i]).CopyTo(b, 48 + 16 * k + 2 * i);
                    }
                    b[80] = live.round; b[81] = live.overtime;
                    if (b.Length > 82)
                    {
                        int q = 82;
                        b[q++] = 4; b[q++] = live.room; b[q++] = live.teamMode; b[q++] = live.theOne;
                        for (int p = 0; p < 4; ++p)
                        {
                            for (int c = 0; c < 3; ++c) b[q++] = live.rgb4[p, c];
                            b[q++] = live.team4[p];
                            BitConverter.GetBytes(live.points4[p]).CopyTo(b, q); q += 2;
                            b[q++] = live.health4[p];
                            b[q++] = live.out4[p];
                        }
                        for (int p = 0; p < 4; ++p) b[q++] = live.nation4[p];
                        for (int p = 2; p < 4; ++p)
                        {
                            for (int i = 0; i < 16 && i < live.name4[p].Length; ++i) b[q + i] = (byte)live.name4[p][i];
                            q += 16;
                            MatchTally s = live.st[p];
                            ushort[] v = { s.throws, s.onTarget, s.hits, s.blocks, s.faced, s.catches, s.strikes,
                                           (ushort)Math.Min(6000f, Math.Max(0f, s.topSpeed * 100f)) };
                            for (int i = 0; i < 8; ++i) { BitConverter.GetBytes(v[i]).CopyTo(b, q); q += 2; }
                        }
                    }
                    RelaySend(R_LIVE, b);
                }
            }
            if (InRoom && now - lastPingSent > 2.0)
            {   // the round trip: the client to its host, the host to each player
                lastPingSent = now;
                var w = new Writer(); Header(w, MSG_PING, session, 0); w.F64(now);
                byte[] d = w.ToArray();
                if (mode == Mode.Client && peerIp != 0) SendTo(d, peerIp, kPort);
                if (mode == Mode.Host)
                    foreach (var m in members) if (!m.local && m.ip != 0) SendTo(d, m.ip, kPort);
            }
            if (InRoom && peerSeen > 0 && now - statStart > 10.0)
            {
                double secs = now - statStart;
                string ping = pingN != 0 ? $", ping {pingSum / pingN:F0} ms ({pingMin:F0} - {pingMax:F0})" : "";
                if (statRecv != 0)
                    Log($"net stats: {(mode == Mode.Host ? "host" : "client")} recv {statRecv / secs:F0} pkt/s, lost {statLost} of {statRecv + statLost} " +
                        $"({100.0 * statLost / Math.Max(1u, statRecv + statLost):F1}%), late/dup {statLate}{ping}");
                ResetStats();
            }
            spectators.RemoveAll(sp => now - sp.seen > 4.0);
            switch (mode)
            {
                case Mode.Spectate:
                {
                    if (now - lastSpectatePing > 0.2)
                    {   // subscribe to every live host we can hear, saying where we are
                        lastSpectatePing = now;
                        foreach (var lg in games)
                        {
                            if (now - lg.seen > 2.5) continue;
                            byte where = presWhere == 0 ? (byte)0 : (presWatch == lg.ip ? (byte)1 : (byte)2);
                            var w = new Writer(); Header(w, MSG_SPECTATE, session, 0); w.U8(where); w.V3(presPos); WriteName(w, name);
                            SendTo(w, lg.ip);
                        }
                    }
                    foreach (var m in matches)
                    {
                        if (!m.fresh) continue;
                        ReadState(new Reader(m.state, 0, m.state.Length), m.game, -1);
                        m.fresh = false;
                        foreach (var lg in games) if (lg.ip == m.ip) m.name = lg.name;
                    }
                    matches.RemoveAll(m => now - m.lastSeen > 4.0);
                    return false;   // the local game is not simulated while spectating
                }
                case Mode.Offline:   // practice: the bot in slot 1 (FOUR CORNERS: bots in the other three corners)
                    g.tun.readyUp = false;   // (practice starts when you are there)
                    if (g.tun.players == 4 ? g.np != 4 || !(g.pl[1].isBot && g.pl[2].isBot && g.pl[3].isBot) : g.np != 2 || !g.pl[1].isBot)
                        g.ResetWithBots(g.tun.players == 4 ? 0xEu : 2u);
                    // a copy, as natively (C++ copies it): in the lobby slot 0 is a bot, whose moves were written into your own
                    // input, so CUSTOMIZE AVATAR's mirror drew the bot at the far end (a Quest 3, 2026-11-14)
                    (@in[0] ??= new PlayerInput()).CopyFrom(local);
                    return true;
                case Mode.Searching:
                    if (now - searchStart > 1.5) { Host(); Log("net: no host found, hosting"); }
                    (@in[0] ??= new PlayerInput()).CopyFrom(local);
                    return true;   // keep playing the bot while searching
                case Mode.Host:
                {
                    // members who went quiet leave; our own input updates our member (we may be in the tank or waiting)
                    var gone = new List<uint>();
                    foreach (var m in members) if (!m.local && now - m.seen > kMemberTimeout) gone.Add(m.id);
                    foreach (uint id in gone) RemoveMember(id, g);
                    Member me = FindMember(session);
                    if (me != null) { me.@in.CopyFrom(local); me.seen = now; }
                    oneTurns = g.tun.oneTurns;
                    if (promotedAt > 0)
                    {   // a room we took over: did the room come with us?
                        bool anyBack = false;
                        foreach (var m in members) anyBack |= !m.local && m.back;
                        if (anyBack) promotedAt = 0;   // (yes: it's ours now)
                        else
                            foreach (var lg in games)
                            {
                                // nobody followed, and the old host still announces its room: it never went quiet, only our
                                // own connection did. Back to it (a copy of the room under the same name helps nobody)
                                if (lg.ip != prevHost || lg.session != prevHostId || lg.seen < promotedAt + 0.5) continue;
                                Log("net: the old host is still there: we go back to its room");
                                Heir h = HeirOf(prevHostId, prevHost, lg.name);
                                StepDown();
                                mode = Mode.Client; room = new RoomInfo(); heirs.Clear();
                                Follow(h);
                                Notice("BACK TO THE ROOM - ITS HOST WAS STILL THERE");
                                return false;
                            }
                    }
                    // IDLE ROOMS: who has moved lately; a player gone still leaves the tank; a room gone still closes
                    foreach (var m in members)
                    {
                        PlayerInput mi = m.local ? local : m.@in;
                        bool hadHead = (m.refTracked & 1) != 0;
                        byte why = Stirred(m.@ref, ref m.refButtons, ref m.refTracked, mi);
                        if (why != StirNone) { m.active = now; if (m.moves[why] < 65535) ++m.moves[why]; }
                        if (hadHead && !mi.headTracked && m.lost < 65535) ++m.lost;
                        if (!hadHead && mi.headTracked && m.found < 65535) ++m.found;
                    }
                    // (diagnostics, every minute: how long each person in the room has been still and what last counted as
                    // moving, so a host's log says why a room stayed open; no positions, only counts)
                    if (now - lastIdleLog >= 60.0)
                    {
                        lastIdleLog = now;
                        foreach (var m in members)
                        {
                            Log($"net: idle check: {(m.local ? "the host" : (m.name.Length > 0 ? m.name : "a player"))} still {now - m.active:F0} s " +
                                $"(moves: head {m.moves[StirHead]}, hands {m.moves[StirHand]}, buttons {m.moves[StirButton]}; " +
                                $"head tracking {((m.refTracked & 1) != 0 ? "on" : "off")}, lost {m.lost}, found {m.found})");
                            Array.Clear(m.moves, 0, m.moves.Length);
                            m.lost = m.found = 0;
                        }
                    }
                    for (int p = 0; p < np_; ++p)
                    {
                        Member m = FindMember(slot[p]);
                        if (m == null || now - m.active < awayAfter) continue;
                        string msg = m.local ? "YOU WERE AWAY - MOVED TO THE WAITING AREA" : $"{(m.name.Length > 0 ? m.name : "A PLAYER")} WAS AWAY - MOVED OUT";
                        Log($"net: {(m.local ? "the host" : m.name)} stood still for {now - m.active:F0} s: out of the tank");
                        for (int k = 0; k < kWireSlots; ++k) if (slot[k] == m.id) slot[k] = 0;
                        CompactTank(g, true);
                        Notice(msg);
                        break;
                    }
                    queueIds.RemoveAll(id => { Member m = FindMember(id); return m == null || now - m.active >= awayAfter; });
                    {
                        double last = 0;
                        foreach (var m in members) last = Math.Max(last, m.active);
                        double idle = now - last;
                        if (idle >= idleClose)
                        {
                            var w = new Writer(); Header(w, MSG_CLOSING, session, 0); w.U16(0);
                            byte[] d = w.ToArray();
                            foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
                            Log($"net: nobody moved for {idle:F0} s: the room closes");
                            CloseRoom();
                            members.Clear();   // (so Leave closes it rather than handing it over)
                            Leave();
                            Notice("ROOM CLOSED - NOBODY WAS PLAYING");
                            return false;
                        }
                        if (idle >= idleClose - 60.0 && now - lastClosingWarn >= 10.0)
                        {
                            lastClosingWarn = now;
                            int secs = Math.Max(1, (int)Math.Ceiling(idleClose - idle));
                            var w = new Writer(); Header(w, MSG_CLOSING, session, 0); w.U16((ushort)secs);
                            byte[] d = w.ToArray();
                            foreach (var m in members) if (!m.local) SendTo(d, m.ip, kPort);
                            Notice($"ROOM CLOSING IN {secs} SECONDS - MOVE TO STAY");
                        }
                        else if (idle < idleClose - 60.0) lastClosingWarn = 0;
                    }
                    if (now - lastHeirs > 1.0) SendHeirs();
                    if (forfeitId != 0)
                    {   // a player gave up: out to the waiting area, the bot or the queue takes the place
                        uint id = forfeitId;
                        forfeitId = 0;
                        if (InTank(id))
                        {
                            for (int p = 0; p < kWireSlots; ++p) if (slot[p] == id) slot[p] = 0;
                            CompactTank(g, true);
                            Notice("PLAYER FORFEITED");
                            Log("net: forfeit -> waiting area");
                        }
                    }
                    if ((g.tun.players == 4 ? 4 : 2) != g.np || np_ != g.np) CompactTank(g, true);   // the rules changed the places
                    FillTank(g);
                    // rotation: a finished match of two humans or with someone queued empties the tank after a short pause; a
                    // room for four, after every match (owner, 2026-11-03: everyone back to the waiting area, JOIN QUEUE again)
                    bool rotateRule = Humans() == np_ || (Humans() >= 1 && (queueIds.Count > 0 || np_ == 4));
                    g.tun.readyUp = np_ == 4;   // READY UP: a room for four starts when every person in the tank pressed A
                    if (rotateRule && g.match == MatchState.MatchSummary)
                    {
                        if (rotateAt == 0) { rotateAt = now + kRotateDelay; Notice("NEXT PLAYERS UP SOON"); }
                        else if (now > rotateAt) Rotate(g);
                    }
                    else if (g.match != MatchState.MatchSummary) rotateAt = 0;
                    HostRoomInfo();
                    if (now - lastBeacon > 0.5)
                    {
                        lastBeacon = now;
                        int waiting = room.members.Count;
                        var w = new Writer(); Header(w, MSG_BEACON, session, 0);   // beacons stay out of the sequence (loss stats)
                        WriteName(w, roomName);
                        w.Bool(g.tun.advanced); w.Bool(members.Count >= np_ + kMaxWaiting);
                        w.I16((short)g.TeamScore(0)); w.I16((short)g.TeamScore(1)); w.F32(g.roundClock);
                        w.U8((byte)Math.Min(255, spectators.Count));
                        w.U8((byte)waiting); w.U8((byte)queueIds.Count); w.U8((byte)Humans());
                        w.Bool(g.tun.shields); w.I8((sbyte)g.tun.knuckleMode);   // MATCH OPTIONS (0.3.8)
                        w.F32(g.tun.basicMatchTime); w.F32(g.tun.advancedRoundTime);
                        w.Bool(g.tun.experimental); w.U8((byte)g.tun.expRoom);   // protocol 9 (native 0.3.12)
                        w.U8((byte)g.np); w.U8((byte)g.tun.teamMode);
                        for (int t = 0; t < 4; ++t) w.I16((short)(t < g.TeamCount() ? g.TeamScore(t) : 0));   // the teams' scores
                        w.U32(prevHost);   // protocol 10: a room handed over says from whom (its spectators follow it)
                        SendTo(w.ToArray(), Broadcast, kPort);
                    }
                    if (now - lastRoomSent > 1.0 / 15.0) { lastRoomSent = now; SendRoom(); }
                    // the game is stepped with the tank players' inputs; an empty place = a bot (Game fills its input)
                    for (int p = 0; p < kWireSlots && p < @in.Length; ++p)
                    {
                        @in[p] = new PlayerInput();
                        if (p < g.seenLate.Length) g.seenLate[p] = 0f;
                        if (slot[p] == 0 || p >= np_) continue;
                        Member m = FindMember(slot[p]);
                        if (m != null)
                        {
                            @in[p] = m.local ? local.Clone() : m.@in.Clone();
                            // CATCHES ONLINE: how late this player sees the balls; HAND TIMES: when they took this input
                            if (!m.local && m.rttMs > 0 && p < g.seenLate.Length) g.seenLate[p] = m.rttMs / 1000f;
                            @in[p].sampleT = !m.local && m.sampleAt >= 0 ? (float)(g.now - (now - m.sampleAt)) : -1f;
                        }
                    }
                    if (rotateRule && g.match == MatchState.MatchSummary)   // no skipping the rotation with A (A is READY before a match)
                        for (int p = 0; p < kWireSlots && p < @in.Length; ++p) @in[p].buttonA = false;
                    if (Humans() == 0) return false;   // empty tank: the arena idles until someone queues
                    if (resumeWait > 0)
                    {   // a match taken over: paused until its players have found us (or the wait is up)
                        bool all = true;
                        for (int p = 0; p < np_; ++p) { Member m = FindMember(slot[p]); if (m != null) all &= m.back; }
                        if (!all && now < resumeWait) return false;
                        resumeWait = 0;
                        Notice("THE MATCH GOES ON");
                        Log($"net: the match goes on ({(all ? "everyone is back" : "waited long enough")})");
                    }
                    byte bots = WantBots();
                    byte nowBots = 0;
                    for (int p = 0; p < g.np; ++p) nowBots |= g.pl[p].isBot ? (byte)(1 << p) : (byte)0;
                    if (nowBots != bots) g.ResetWithBots(bots);
                    return true;   // caller steps; snapshot is sent via SendState
                }
                case Mode.Client:
                {
                    // the snapshots do not say who is a bot: take it from the room
                    for (int p = 0; p < kWireSlots; ++p) g.pl[p].isBot = room.IsBot(p);
                    if (LocalSlot() < 0)
                        foreach (var lg in games) if (lg.ip == peerIp && now - lg.seen < 3.0) g.tun.advanced = lg.advanced;
                    if (peerIp != 0)
                    {   // (none while we look for where our room went)
                        var w = new Writer(); Header(w, MSG_INPUT, session, ++seq); WriteInput(w, local);
                        w.F64(now);   // HAND TIMES: when this was taken, on our clock (older hosts stop reading before it)
                        SendTo(w, peerIp);
                    }
                    bool known = LocalSlot() >= 0;
                    foreach (var m in room.members) known |= m.id == session;
                    if (!known && !migrating && peerIp != 0 && now - lastJoin > 1.0) SendJoin(joinPlay);   // the JOIN may have been lost: ask again until the room lists us
                    int s = LocalSlot();
                    if (gotState) stateAt = now;
                    // BALL LOOK-AHEAD (Game.LookAhead, native 0.3.15): the balls one round trip ahead of the host's latest
                    // snapshot. Each frame starts again from that snapshot (a frame without a new one too: the last
                    // frame's balls were already ahead)
                    bool ahead = s >= 0 && lookAhead && pingAvg > 0 && !migrating && lastState.Length > 0 && now - stateAt < 0.5;
                    if (gotState || ahead) ReadState(new Reader(lastState, 0, lastState.Length), g, s);
                    var swap = g.events;
                    g.events = pendingEvents;
                    pendingEvents = swap;
                    pendingEvents.Clear();
                    if (s >= 0) g.pl[s].@in.CopyFrom(local);
                    if (ahead)
                    {
                        // how far: to where the host's clock will be when what I do now reaches it. Its snapshots say its
                        // clock; the quickest of them tells how its clock runs against ours (a late packet only looks
                        // further behind), so the lead runs smoothly however unevenly they arrive (owner's test, 2026-11-05:
                        // the ball shook when the lead came from each snapshot's own arrival). Hosts before 0.3.15: the
                        // round trip and the snapshot's age
                        float lead = pingAvg / 1000f + (float)(now - stateAt);
                        if (g.ahead.snapNow >= 0)
                        {
                            if (gotState)
                            {
                                double off = g.ahead.snapNow - now;
                                if (!hostOffSet || off > hostOff || off < hostOff - 1.0) { hostOff = off; hostOffSet = true; }
                            }
                            hostOff -= dt * 0.005;   // (clocks drift a little: the quickest packets keep it honest)
                            lead = (float)(now + hostOff - g.ahead.snapNow) + pingAvg / 1000f;
                        }
                        g.LookAhead(s, lead, dt);
                    }
                    if (migrating)
                    {   // HOST HANDOFF: knocking on the new host's door until it answers (or the next one's)
                        // one of the people in line already hosts the room (its beacon says so): straight there
                        for (int i = 0; i < heirs.Count; ++i)
                            if (heirs[i].id != peerSession && HeirBeacon(heirs[i].id, heirs[i].ip, migrateFrom)) { heirAt = i + 1; Follow(heirs[i]); break; }
                        if (scoutUntil > 0)
                        {   // (woken up: nobody hosts it any more)
                            if (now > scoutUntil) { Log("net: our room is gone"); DropRoom("THE ROOM HAS CLOSED"); }
                        }
                        else
                        {
                            if (now - lastJoin > 0.5) SendJoin(false);
                            if (now > migrateUntil) NextHeir(g);
                        }
                    }
                    else if (!PeerConnected && now - peerSeen > kHostQuiet && WokeAndHostThere())
                    {   // WOKEN UP (native 8f1fbdc; the owner's session, 2026-11-14: four times a headset back from sleep took
                        // over a room whose host was still there, for a second, and went back): the host still announces its
                        // room, so it is there and only we were away. Keep asking it to let us back in (at most kWakeKnock)
                        if (now - lastJoin > 1.0) SendJoin(joinPlay);
                        if (now - peerSeen > kHostQuiet + 1.0 && NoticeText == null) Notice("REJOINING THE ROOM");
                    }
                    else if (!PeerConnected && now - peerSeen > kHostQuiet)
                    {
                        if (wokeAt > 0) Log("net: back from sleep, the host stopped announcing its room");
                        wokeAt = 0;
                        if (heirs.Count > 0)
                        {   // the host is gone: the room goes on with the next in line
                            Log("net: host lost: handing the room over");
                            Notice("THE HOST LEFT - HANDING THE ROOM OVER");
                            prevHost = peerIp; prevHostId = peerSession; heirAt = 0;
                            NextHeir(g);
                        }
                        else
                        {
                            Log("net: host lost");
                            DropRoom("CONNECTION TO HOST LOST");
                        }
                    }
                    else if (now - peerSeen > 1.5 && heirs.Count > 0 && NoticeText == null)
                        Notice("HOST NOT RESPONDING - HOLD ON");
                    return false;
                }
            }
            return true;
        }

        // one game packet (LAN, or unwrapped from the relay); returns true for a new snapshot from our host
        bool Handle(byte[] pkt, int start, int end, uint fromIp, Game g)
        {
            var r = new Reader(pkt, start, end);
            uint magic = r.U32(); byte ver = r.U8(), type = r.U8();
            uint sess = r.U32(), sq = r.U32();
            if (r.ok && magic == kMagic && ver != kVersion && type == MSG_BEACON && sess != session)
            {
                if (otherVer != ver) Log($"net: a game on protocol v{ver} is on the network (this build is v{kVersion})");
                otherVer = ver; otherVerSeen = now;
                return false;
            }
            if (!r.ok || magic != kMagic || ver != kVersion || sess == session) return false;   // ignore our own broadcasts
            if (online && sess != 0)
            {   // (MUTED PLAYERS: whose virtual address is whose)
                if (addrOf.Count > 512) addrOf.Clear();
                addrOf[sess] = fromIp;
            }
            if (mode == Mode.Client && (type == MSG_ROOM || type == MSG_STATE) && sess != peerSession && now - peerSeen > 3.0)
            {   // our host has gone quiet and the next in line already hosts the room: follow them now
                for (int i = 0; i < heirs.Count; ++i)
                {
                    if (heirs[i].id != sess || heirs[i].ip != fromIp) continue;
                    if (!migrating) { prevHost = peerIp; prevHostId = peerSession; }
                    heirAt = i + 1;
                    Follow(heirs[i]);
                    break;
                }
            }
            // a client only listens to its host's session; a host only to its members (by address + session)
            bool fromHost = mode == Mode.Client && fromIp == peerIp && (peerSession == 0 || sess == peerSession);
            Member fromMember = null;
            if (mode == Mode.Host) { Member m = FindMember(sess); if (m != null && !m.local && m.ip == fromIp) fromMember = m; }

            if (type == MSG_BEACON)
            {
                var lg = new LanGame { ip = fromIp, session = sess, seen = now };
                var nm = new char[16];
                for (int i = 0; i < 16; ++i) nm[i] = (char)r.U8();
                lg.advanced = r.U8() != 0; lg.full = r.U8() != 0;
                lg.score0 = r.I16(); lg.score1 = r.I16(); lg.clock = r.F32(); lg.spectators = r.U8();
                lg.waiting = r.U8(); lg.queued = r.U8(); lg.inTank = r.U8();
                if (r.ok && r.Left >= 10)
                {   // MATCH OPTIONS (0.3.8 hosts)
                    lg.shields = r.U8() != 0;
                    lg.knuckleMode = Math.Max(-1, Math.Min(1, (int)r.I8()));
                    lg.matchTime = r.F32(); lg.roundTime = r.F32();
                    if (!float.IsFinite(lg.matchTime) || lg.matchTime < 0) lg.matchTime = 180f;
                    if (!float.IsFinite(lg.roundTime) || lg.roundTime < 0) lg.roundTime = 0f;
                }
                if (r.ok && r.Left >= 12)
                {   // protocol 9 (native 0.3.12): the EXPERIMENTAL room, its players and teams, the teams' scores
                    lg.experimental = r.U8() != 0;
                    lg.expRoom = Math.Min((int)r.U8(), 2);
                    lg.players = r.U8() == 4 ? 4 : 2;
                    lg.teamMode = Math.Min(2, (int)r.U8());
                    for (int t = 0; t < 4; ++t) lg.teamScores[t] = r.I16();
                }
                if (r.ok && r.Left >= 4) lg.continues = r.U32();   // protocol 10: handed over from that host
                for (int i = 0; i < 16; ++i) if (nm[i] != 0 && (nm[i] < 32 || nm[i] > 'Z')) nm[i] = ' ';
                lg.name = CStr(nm);
                if (r.ok)
                {
                    bool found = false;
                    for (int i = 0; i < games.Count; ++i) if (games[i].ip == lg.ip) { games[i] = lg; found = true; }
                    if (!found && games.Count < 16) games.Add(lg);
                }
                if (mode == Mode.Searching && r.ok && !lg.full && lg.Playable) Join(lg.ip);
            }
            else if (type == MSG_SPECTATE && mode == Mode.Host)
            {
                byte where = r.U8(); Vec3 pos = r.V3();
                if (!r.ok || !IsFinite(pos)) { where = 2; pos = default; }
                string who = "";
                if (r.ok && r.Left >= 16) who = ReadName(r);   // 0.3.1+
                Spectator sp = null;
                foreach (var x in spectators) if (x.ip == fromIp && x.id == sess) sp = x;
                if (sp == null && spectators.Count < 24)
                {
                    sp = new Spectator { ip = fromIp, id = sess, seen = now, where = where, pos = pos };
                    spectators.Add(sp);
                    Log($"net: spectator {IpStr(fromIp)} watching");
                    if (where == 1) Notice("A SPECTATOR IS WATCHING");
                }
                if (sp != null) { sp.seen = now; sp.where = where; sp.pos = pos; if (who.Length > 0) sp.name = who; }
            }
            else if ((type == MSG_STATE || type == MSG_ROOM) && mode == Mode.Spectate)
            {
                SpecMatch m = null;
                foreach (var x in matches) if (x.ip == fromIp) m = x;
                if (m == null && matches.Count < 8)
                {
                    m = new SpecMatch { ip = fromIp };
                    foreach (var lg in games) if (lg.ip == m.ip) m.name = lg.name;
                    matches.Add(m);
                    Log($"net: watching {IpStr(fromIp)}");
                }
                if (m != null)
                {
                    m.lastSeen = now;
                    if (type == MSG_ROOM) { if (ReadRoom(r, ref m.room, ref m.lastRoomSeq)) LearnAddrs(m.room); }
                    else
                    {
                        ReadEvents(r, ref m.lastEventId, m.events);
                        if (m.events.Count > 64) m.events.RemoveRange(0, m.events.Count - 64);
                        if (sq > m.lastSeq) { m.lastSeq = sq; m.state = Slice(r); m.fresh = true; m.stateSeen = now; }
                    }
                }
            }
            else if (type == MSG_LEAVE)
            {
                if (mode == Mode.Host && fromMember != null) RemoveMember(sess, g);
                else if (fromHost)
                {
                    Log("net: host closed the arena");
                    DropRoom("HOST CLOSED THE ARENA");
                }
            }
            else if (type == MSG_REJECT && fromHost)
            {
                var why = new char[32];
                for (int i = 0; i < 32; ++i) { char c = (char)r.U8(); why[i] = c != 0 && (c < 32 || c > 'Z') ? ' ' : c; }
                string w = CStr(why);
                Log($"net: host refused: {w}");
                DropRoom(w.Length > 0 ? w : "ARENA FULL");
            }
            else if (type == MSG_JOIN && mode == Mode.Host)
            {
                r.U8();   // "play now" from older builds: everyone now starts in the room's waiting area
                string jname = r.Left >= 16 ? ReadName(r) : "";   // 0.3.1+
                int jver = 0;   // (native 0.3.20+: their version, FAIR MATCHES; older: 0)
                if (r.ok && r.Left >= 1)
                {
                    var v = new System.Text.StringBuilder();
                    int n = r.U8();
                    for (int i = 0; i < n && r.ok && r.Left >= 1; ++i) { char c = (char)r.U8(); if (i < 15) v.Append(c); }
                    if (r.ok) jver = VersionNumber(v.ToString());
                }
                Member known = FindMember(sess);
                if (known != null)
                {   // (again: woken up, or following a new host; their version as they say now)
                    if (known.ip == fromIp) { known.seen = now; known.back = true; known.ver = jver; }
                    return false;
                }
                if (members.Count >= np_ + kMaxWaiting) { Reject(fromIp, "ARENA FULL"); return false; }
                var m = new Member { ip = fromIp, id = sess, seen = now, active = now, name = jname, ver = jver };
                bool outed = false;   // voted out of this arena for this session: by session, address or name
                foreach (var x in removed) outed |= x.id == sess || x.ip == fromIp || (x.name.Length > 0 && x.name == m.name);
                if (outed) { Reject(fromIp, "REMOVED FROM THIS ARENA"); return false; }
                members.Add(m);
                CompactTank(g, false);   // ... and gets into the tank through the queue (owner request)
                Notice("A PLAYER IS WAITING");
                peerSeen = now;
                Log($"net: {IpStr(fromIp)} entered the room ({members.Count} people)");
            }
            else if (type == MSG_PING && (fromHost || fromMember != null))
            {   // answer at once with the sender's own clock
                double t = r.F64();
                if (r.ok) { var w = new Writer(); Header(w, MSG_PONG, session, 0); w.F64(t); SendTo(w, fromIp); }
            }
            else if (type == MSG_PONG && (fromHost || fromMember != null))
            {
                double t = r.F64();
                float ms = (float)((now - t) * 1000.0);
                if (r.ok && ms >= 0f && ms < 5000f)
                {
                    if (pingN == 0 || ms < pingMin) pingMin = ms;
                    if (pingN == 0 || ms > pingMax) pingMax = ms;
                    pingSum += ms;
                    ++pingN;
                    lastPingMs = ms;
                    // (BALL LOOK-AHEAD's lead: the round trip, smoothed; a passing stall moves it only a little)
                    pingAvg = pingAvg <= 0 ? ms : pingAvg + (Math.Min(ms, pingAvg * 1.5f + 20f) - pingAvg) * 0.25f;
                    if (fromMember != null)   // (host: each player's own, for CATCHES ONLINE)
                        fromMember.rttMs = fromMember.rttMs <= 0 ? ms
                                         : fromMember.rttMs + (Math.Min(ms, fromMember.rttMs * 1.5f + 20f) - fromMember.rttMs) * 0.25f;
                }
            }
            else if (type == MSG_INPUT && fromMember != null)
            {
                fromMember.seen = now;
                fromMember.back = true;
                peerSeen = now;
                if (sq > fromMember.lastSeq)
                {
                    fromMember.lastSeq = sq;
                    ReadInput(r, fromMember.@in);
                    if (InTank(fromMember.id)) CountSeq(sq);
                    // HAND TIMES (native 0.3.19): the player's clock when they took it, after the input (older: none)
                    if (r.ok && r.Left >= 8)
                    {
                        double ct = r.F64();
                        Member m = fromMember;
                        if (!double.IsNaN(ct) && !double.IsInfinity(ct))
                        {
                            if (m.clockSet) m.clockOff -= (now - m.clockAt) * 0.005;   // (clocks drift a little)
                            double off = ct - now;
                            if (!m.clockSet || off > m.clockOff || off < m.clockOff - 1.0) { m.clockOff = off; m.clockSet = true; }
                            m.clockAt = now;
                            m.sampleAt = ct - m.clockOff;
                        }
                    }
                }
            }
            else if (type == MSG_KICKVOTE && fromMember != null)
            {
                uint target = r.U32();
                if (r.ok) CountKickVote(target, sess);
            }
            else if (type == MSG_QUEUE && fromMember != null)
            {
                byte v = r.U8();
                if (v == 2) { forfeitId = sess; return false; }
                bool want = v != 0;
                int at = queueIds.IndexOf(sess);
                if (want && at < 0 && !InTank(sess) && queueIds.Count < np_) queueIds.Add(sess);   // full: stays waiting
                if (!want && at >= 0) queueIds.RemoveAt(at);
                HostRoomInfo();
            }
            else if (type == MSG_VOICE)
            {
                r.U32(); r.U8();
                if (!r.ok || r.Left < kVoiceSamples) return false;
                var pcm = new byte[kVoiceSamples];
                Array.Copy(pkt, r.p, pcm, 0, kVoiceSamples);
                if (mode == Mode.Host)
                {   // a member or a spectator at this arena talks: pass it on to the room
                    Spectator sp = null;
                    foreach (var x in spectators) if (x.ip == fromIp && x.id == sess && x.where == 1) sp = x;
                    if (fromMember != null)
                        ForwardVoice(sess, InTank(sess) ? VOICE_PLAYER : VOICE_WAITING, pcm, fromIp, sess);
                    else if (sp != null)
                        ForwardVoice(sess, VOICE_SPECTATOR, pcm, fromIp, sess);
                }
                else if (fromHost || (mode == Mode.Spectate && presWhere == 1 && fromIp == presWatch))
                {
                    var h = new Reader(pkt, start + 14, end);   // re-read the speaker / role the host filled in
                    var f = new VoiceFrame { speaker = h.U32(), role = h.U8() };
                    if (f.speaker != session && f.role <= VOICE_SPECTATOR && voiceIn.Count < 64)
                    {
                        Array.Copy(pcm, f.data, kVoiceSamples);
                        voiceIn.Add(f);
                    }
                }
            }
            else if (type == MSG_CAMERAS)
            {
                int body = r.p;
                uint origin = r.U32();
                var team = new char[16];
                for (int i = 0; i < 16; ++i) team[i] = (char)r.U8();
                ushort len = r.U16();
                if (!r.ok || len > 400 || r.Left < len) return false;
                if (mode == Mode.Host)
                {   // pass a teammate's cameras on to our spectators (not back to the sender)
                    var w = new Writer(); Header(w, MSG_CAMERAS, session, 0);
                    w.Bytes(pkt, body, r.p + len - body);
                    byte[] d = w.ToArray();
                    foreach (var sp in spectators) if (sp.id != origin) SendTo(d, sp.ip, kPort);
                }
                else if (mode == Mode.Spectate && origin != session)
                {
                    camTeam = CStr(team);
                    camBlob = new byte[len];
                    Array.Copy(pkt, r.p, camBlob, 0, len);
                    camFrom = origin;
                    camFresh = true;
                }
            }
            else if (type == MSG_CROWD)
            {
                if (fromHost) ReadCrowd(pkt, r.p, r.e, crowd, now);
                else if (mode == Mode.Spectate)
                    foreach (var m in matches) if (m.ip == fromIp) ReadCrowd(pkt, r.p, r.e, m.crowd, now);
            }
            else if (type == MSG_HEIRS && fromHost && !migrating)
            {   // who hosts next, should our host go
                byte f = r.U8();
                string rn = ReadName(r);
                byte n = r.U8();
                var v = new List<Heir>();
                for (int i = 0; i < n && i < 12 && r.ok; ++i)
                {
                    var h = new Heir { id = r.U32(), ip = r.U32() };
                    h.name = ReadName(r);
                    if (r.ok && h.id != 0 && h.ip != 0) v.Add(h);
                }
                var outed = new List<Heir>();   // (the people the host removed: in HEIRS since native 0.3.13's review)
                byte k = r.p < r.e ? r.U8() : (byte)0;
                for (int i = 0; i < k && i < 16 && r.ok; ++i)
                {
                    var h = new Heir { id = r.U32(), ip = r.U32() };
                    h.name = ReadName(r);
                    if (r.ok) outed.Add(h);
                }
                if (r.ok) { heirs = v; heirRemoved = outed; heirFlags = f; heirRoom = rn; }
            }
            else if (type == MSG_HANDOFF && fromHost)
            {   // the host hands the room to someone (maybe us)
                uint target = r.U32();
                bool stays = r.U8() != 0;
                if (!r.ok || target == peerSession) return false;
                prevHost = peerIp; prevHostId = peerSession;
                if (target == session) { Promote(g, stays, peerIp, peerSession); return false; }
                for (int i = 0; i < heirs.Count; ++i)
                    if (heirs[i].id == target) { heirAt = i + 1; Follow(heirs[i]); break; }
            }
            else if (type == MSG_CLOSING && fromHost)
            {   // IDLE ROOMS: the room is about to close (0: it has)
                ushort secs = r.U16();
                if (!r.ok) return false;
                if (secs == 0)
                {
                    Log("net: the room closed (nobody moved)");
                    DropRoom("ROOM CLOSED - NOBODY WAS PLAYING");
                }
                else Notice($"ROOM CLOSING IN {secs} SECONDS - MOVE TO STAY");
            }
            else if (type == MSG_ROOM && fromHost)
            {
                peerSession = sess;
                peerSeen = now;
                if (migrating) Arrived();
                if (ReadRoom(r, ref room, ref lastRoomSeq)) LearnAddrs(room);
            }
            else if (type == MSG_STATE && fromHost)
            {
                peerSession = sess;   // the host's session, learned from its beacon or first snapshot
                peerSeen = now;
                if (migrating) Arrived();
                ReadEvents(r, ref lastEventId, pendingEvents);   // every packet, even one overtaken by a newer snapshot
                if (sq > lastRecvSeq) { CountSeq(sq); lastState = Slice(r); return true; }
                ++statLate;
            }
            return false;
        }

        static byte[] Slice(Reader r)
        {
            var o = new byte[Math.Max(0, r.e - r.p)];
            Array.Copy(r.d, r.p, o, 0, o.Length);
            return o;
        }

        // host: the post-step snapshot to every member and spectator
        public void SendState(Game g)
        {
            if (mode != Mode.Host) return;
            foreach (var e in g.events) eventLog.AddLast(new SentEvent { id = nextEventId++, t = now, e = e });
            while (eventLog.Count > 0 && (now - eventLog.First.Value.t > 0.3 || eventLog.Count > 24)) eventLog.RemoveFirst();
            bool anyone = spectators.Count > 0;
            foreach (var m in members) anyone |= !m.local;
            if (!anyone) return;
            // full rate (every frame) to the two players in the tank; 30 Hz to the people outside and spectators
            // (bandwidth, and the relay's per-player rate limit)
            bool slow = now - lastSlowState >= 1.0 / 30.0;
            if (slow) lastSlowState = now;
            var w = new Writer(); Header(w, MSG_STATE, session, ++seq); WriteEvents(w, eventLog); WriteState(w, g);
            byte[] d = w.ToArray();
            foreach (var m in members)
            {
                if (m.local) continue;
                bool player = InTank(m.id);   // (all four corners)
                if (player || slow) SendTo(d, m.ip, kPort);
            }
            if (slow) foreach (var sp in spectators) SendTo(d, sp.ip, kPort);
        }
    }
}
