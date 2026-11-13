using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

internal sealed class DeviceState
{
    public string Name;
    public int? Percent;
    public bool Charging;
    public bool Online;
    public bool Stale;
    public DateTime Seen;
    public string Detail;
}

internal static class Hid
{
    public const uint GenericRead = 0x80000000;
    public const uint GenericWrite = 0x40000000;
    public const uint Share = 3;
    public const uint OpenExisting = 3;
    public const uint Overlapped = 0x40000000;
    public static readonly IntPtr Invalid = new IntPtr(-1);

    public struct DeviceHit
    {
        public ushort Vendor;
        public ushort Product;
        public ushort UsagePage;
        public ushort Usage;
        public ushort OutputLength;
        public string Path;
        public string Name;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct InterfaceData
    {
        public int cbSize;
        public Guid ClassGuid;
        public int Flags;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Attributes
    {
        public int Size;
        public ushort VendorId;
        public ushort ProductId;
        public ushort Version;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct Caps
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputLength;
        public ushort OutputLength;
        public ushort FeatureLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort LinkNodes;
        public ushort InputButtonCaps;
        public ushort InputValueCaps;
        public ushort InputDataIndices;
        public ushort OutputButtonCaps;
        public ushort OutputValueCaps;
        public ushort OutputDataIndices;
        public ushort FeatureButtonCaps;
        public ushort FeatureValueCaps;
        public ushort FeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeOverlapped
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public int Offset;
        public int OffsetHigh;
        public IntPtr Event;
    }

    [DllImport("hid.dll")]
    public static extern void HidD_GetHidGuid(out Guid guid);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr handle, ref Attributes attributes);

    [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr handle, byte[] buffer, int length);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr handle, out IntPtr preparsed);

    [DllImport("hid.dll")]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsed);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsed, ref Caps caps);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_SetNumInputBuffers(IntPtr handle, int count);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetFeature(IntPtr handle, byte[] buffer, int length);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref InterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref InterfaceData data, IntPtr detail, int size, out int required, IntPtr info);

    [DllImport("setupapi.dll")]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadFile(IntPtr handle, byte[] buffer, int length, out int read, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool WriteFile(IntPtr handle, byte[] buffer, int length, out int written, ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetOverlappedResult(IntPtr handle, ref NativeOverlapped overlapped, out int transferred, bool wait);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CancelIo(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateEvent(IntPtr attributes, bool manualReset, bool initial, string name);

    [DllImport("kernel32.dll")]
    public static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    public static List<string> Find(ushort vendor, ushort product, ushort usagePage, ushort usage)
    {
        var paths = new List<string>();
        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == Invalid) return paths;
        try
        {
            for (int i = 0; ; i++)
            {
                var data = new InterfaceData();
                data.cbSize = Marshal.SizeOf(typeof(InterfaceData));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                int required;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                    IntPtr handle = CreateFile(path, 0, Share, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (handle == Invalid) continue;
                    try
                    {
                        var attr = new Attributes();
                        attr.Size = Marshal.SizeOf(typeof(Attributes));
                        if (!HidD_GetAttributes(handle, ref attr)) continue;
                        if (attr.VendorId != vendor || attr.ProductId != product) continue;
                        IntPtr preparsed;
                        if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                        var caps = new Caps();
                        bool match = HidP_GetCaps(preparsed, ref caps) == 0x00110000
                            && caps.UsagePage == usagePage && caps.Usage == usage;
                        HidD_FreePreparsedData(preparsed);
                        if (match) paths.Add(path);
                    }
                    finally { CloseHandle(handle); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return paths;
    }

    public static List<DeviceHit> Enumerate()
    {
        var found = new List<DeviceHit>();
        Guid guid;
        HidD_GetHidGuid(out guid);
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == Invalid) return found;
        try
        {
            for (int i = 0; ; i++)
            {
                var data = new InterfaceData();
                data.cbSize = Marshal.SizeOf(typeof(InterfaceData));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                int required;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                    IntPtr handle = CreateFile(path, 0, Share, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
                    if (handle == Invalid) continue;
                    try
                    {
                        var attr = new Attributes();
                        attr.Size = Marshal.SizeOf(typeof(Attributes));
                        if (!HidD_GetAttributes(handle, ref attr)) continue;
                        IntPtr preparsed;
                        if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                        var caps = new Caps();
                        int status = HidP_GetCaps(preparsed, ref caps);
                        HidD_FreePreparsedData(preparsed);
                        if (status != 0x00110000) continue;
                        byte[] nameBytes = new byte[254];
                        string name = "";
                        if (HidD_GetProductString(handle, nameBytes, nameBytes.Length))
                            name = System.Text.Encoding.Unicode.GetString(nameBytes).TrimEnd('\0').Trim();
                        var hit = new DeviceHit();
                        hit.Vendor = attr.VendorId;
                        hit.Product = attr.ProductId;
                        hit.UsagePage = caps.UsagePage;
                        hit.Usage = caps.Usage;
                        hit.OutputLength = caps.OutputLength;
                        hit.Path = path;
                        hit.Name = name;
                        found.Add(hit);
                    }
                    finally { CloseHandle(handle); }
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return found;
    }

    public static IntPtr Open(string path)
    {
        IntPtr handle = CreateFile(path, GenericRead | GenericWrite, Share, IntPtr.Zero, OpenExisting, Overlapped, IntPtr.Zero);
        if (handle == Invalid) return Invalid;
        HidD_SetNumInputBuffers(handle, 64);
        return handle;
    }

    public static bool Write(IntPtr handle, byte[] packet)
    {
        var overlapped = new NativeOverlapped();
        overlapped.Event = CreateEvent(IntPtr.Zero, true, false, null);
        int written;
        bool ok = WriteFile(handle, packet, packet.Length, out written, ref overlapped);
        int error = Marshal.GetLastWin32Error();
        if (!ok && error == 997)
        {
            if (WaitForSingleObject(overlapped.Event, 800) == 0)
                GetOverlappedResult(handle, ref overlapped, out written, false);
            else
            {
                CancelIo(handle);
                written = 0;
            }
        }
        CloseHandle(overlapped.Event);
        return written == packet.Length;
    }

    public static List<byte[]> ReadUntil(IntPtr handle, int length, int milliseconds)
    {
        var packets = new List<byte[]>();
        int deadline = Environment.TickCount + milliseconds;
        while (Environment.TickCount < deadline && packets.Count < 6)
        {
            byte[] buffer = new byte[length];
            var overlapped = new NativeOverlapped();
            overlapped.Event = CreateEvent(IntPtr.Zero, true, false, null);
            int read;
            bool ok = ReadFile(handle, buffer, length, out read, ref overlapped);
            int error = Marshal.GetLastWin32Error();
            if (!ok && error == 997)
            {
                uint left = (uint)Math.Max(1, deadline - Environment.TickCount);
                if (WaitForSingleObject(overlapped.Event, Math.Min(left, 700)) != 0)
                {
                    CancelIo(handle);
                    CloseHandle(overlapped.Event);
                    break;
                }
                GetOverlappedResult(handle, ref overlapped, out read, false);
            }
            CloseHandle(overlapped.Event);
            if (read <= 0) continue;
            byte[] copy = new byte[read];
            Buffer.BlockCopy(buffer, 0, copy, 0, read);
            packets.Add(copy);
        }
        return packets;
    }
}

internal static class Readers
{
    public static DeviceState Keyboard()
    {
        return CompXKeyboard(0xFA09, "Keyboard");
    }

    public static DeviceState CompXKeyboard(ushort product, string name)
    {
        var state = new DeviceState();
        state.Name = name;
        var paths = Hid.Find(0x3554, product, 0xFF02, 0x0002);
        if (paths.Count == 0)
        {
            state.Detail = "receiver unplugged";
            return state;
        }
        state.Online = true;
        IntPtr handle = Hid.Open(paths[0]);
        if (handle == Hid.Invalid)
        {
            state.Detail = "receiver busy";
            return state;
        }
        try
        {
            byte[] ask = new byte[20];
            ask[0] = 0x13;
            ask[1] = 0x05;
            for (int attempt = 0; attempt < 2 && state.Percent == null; attempt++)
            {
                if (!Hid.Write(handle, ask)) continue;
                foreach (byte[] packet in Hid.ReadUntil(handle, 20, 900))
                {
                    if (packet.Length < 20 || packet[0] != 0x13 || packet[1] != 0x0A) continue;
                    int sum = 0;
                    for (int i = 0; i < 19; i++) sum = (sum + packet[i]) & 0xFF;
                    if (sum != packet[19] || packet[6] > 100) continue;
                    state.Percent = packet[6];
                    state.Charging = false;
                    state.Detail = packet[7] == 0x01 ? "2.4 GHz" : "link down";
                    if (packet[7] != 0x01) state.Online = false;
                }
            }
            if (state.Percent == null) state.Detail = "no report — press a key";
        }
        finally { Hid.CloseHandle(handle); }
        return state;
    }

    public static DeviceState Mouse()
    {
        return AttackSharkMouse(0x1D57, 0xFA60, "Mouse");
    }

    public static DeviceState AttackSharkMouse(ushort vendor, ushort product, string name)
    {
        var state = new DeviceState();
        state.Name = name;
        var paths = Hid.Find(vendor, product, 0x000A, 0x0000);
        if (paths.Count == 0)
        {
            state.Detail = "receiver unplugged";
            return state;
        }
        state.Online = true;
        IntPtr handle = Hid.Open(paths[0]);
        if (handle == Hid.Invalid)
        {
            state.Detail = "receiver busy";
            return state;
        }
        try
        {
            foreach (byte[] packet in Hid.ReadUntil(handle, 5, 2500))
            {
                if (packet.Length < 5 || packet[0] != 0x03 || packet[2] != 0x40) continue;
                if (packet[4] > 10) continue;
                state.Percent = packet[4] * 10;
                state.Detail = "2.4 GHz";
            }
            if (state.Percent == null) state.Detail = "asleep — move the mouse";
        }
        finally { Hid.CloseHandle(handle); }
        return state;
    }

    public static DeviceState Headphones()
    {
        return RazerHeadset(0x0552, "Headphones");
    }

    public static DeviceState RazerHeadset(ushort product, string name)
    {
        var state = new DeviceState();
        state.Name = name;
        var paths = Hid.Find(0x1532, product, 0xFF00, 0x0000);
        if (paths.Count == 0)
        {
            state.Detail = "receiver unplugged";
            return state;
        }
        state.Online = true;
        IntPtr handle = Hid.Open(paths[0]);
        if (handle == Hid.Invalid)
        {
            state.Detail = "receiver busy";
            return state;
        }
        try
        {
            if (!Hid.Write(handle, Pa(0x07, 0x0E, 0x02, 0xE1, 1)))
            {
                state.Detail = "no link";
                return state;
            }
            Hid.ReadUntil(handle, 64, 400);
            int? level = Query(handle, 0x21);
            int? charge = Query(handle, 0x2A);
            Hid.Write(handle, Pa(0x07, 0x0E, 0x02, 0xE1, 0));
            Hid.ReadUntil(handle, 64, 250);
            if (level == null)
            {
                state.Detail = "off or out of range";
                state.Online = false;
                return state;
            }
            state.Percent = level;
            state.Charging = charge == 1;
            state.Detail = state.Charging ? "charging" : "2.4 GHz";
        }
        finally { Hid.CloseHandle(handle); }
        return state;
    }

    static int? Query(IntPtr handle, byte command)
    {
        if (!Hid.Write(handle, Pa(8, 0x08, 0x03, command, 0))) return null;
        foreach (byte[] packet in Hid.ReadUntil(handle, 64, 700))
        {
            if (packet.Length < 17 || packet[0] != 0x01 || packet[13] != command) continue;
            if (packet[14] != 0x01 && packet[14] != 0x02) continue;
            int size = packet[15];
            if (size < 1 || 16 + size > packet.Length) continue;
            int value = packet[16];
            if (command == 0x21 && value <= 100) return value;
            if (command == 0x2A && value <= 1) return value;
        }
        return null;
    }

    static byte[] Pa(byte length, byte marker, byte type, byte command, byte argument)
    {
        byte[] frame = new byte[64];
        frame[0] = 0x01;
        frame[1] = 0x80;
        frame[2] = length;
        frame[3] = 0x50;
        frame[4] = 0x41;
        frame[5] = marker;
        frame[6] = 0x08;
        frame[7] = type;
        frame[8] = command;
        frame[9] = argument;
        return frame;
    }
}

internal sealed class ExtraDevice
{
    public string Key;
    public string Mark;
    public int QuietSeconds;
    public string Name;
    public Func<DeviceState> Read;
}

internal static class Catalog
{
    static readonly ushort[] RazerHeadsets = { 0x053A };
    static readonly HashSet<string> LogitechTried = new HashSet<string>();
    static readonly object ProbeGate = new object();

    public static List<ExtraDevice> Discover()
    {
        var extras = new List<ExtraDevice>();
        List<Hid.DeviceHit> hits;
        try { hits = Hid.Enumerate(); }
        catch (Exception) { return extras; }
        var seen = new HashSet<string>();
        foreach (Hid.DeviceHit hit in hits)
        {
            if (hit.Vendor == 0x3554 && hit.Product != 0xFA09 && hit.UsagePage == 0xFF02 && hit.Usage == 0x0002)
                Add(extras, seen, CompX(hit));
            if (hit.UsagePage == 0x000A && hit.Usage == 0x0000 && !(hit.Vendor == 0x1D57 && hit.Product == 0xFA60))
            {
                if (hit.Vendor == 0x1D57 || hit.Vendor == 0x3554)
                    Add(extras, seen, Shark(hit));
            }
            if (hit.Vendor == 0x1532 && hit.UsagePage == 0xFF00 && hit.Usage == 0x0000 && hit.Product != 0x0552)
            {
                for (int i = 0; i < RazerHeadsets.Length; i++)
                {
                    if (hit.Product != RazerHeadsets[i]) continue;
                    Add(extras, seen, Razer(hit));
                }
            }
        }
        AddLogitech(extras, seen, hits);
        return extras;
    }

    static void Add(List<ExtraDevice> extras, HashSet<string> seen, ExtraDevice device)
    {
        if (device == null || seen.Contains(device.Key)) return;
        seen.Add(device.Key);
        extras.Add(device);
    }

    static ExtraDevice CompX(Hid.DeviceHit hit)
    {
        ushort product = hit.Product;
        string name = Nice(hit.Name, "Keyboard");
        var device = new ExtraDevice();
        device.Key = "3554:" + product.ToString("X4");
        device.Mark = "K";
        device.QuietSeconds = 10 * 60;
        device.Name = name;
        device.Read = delegate { return Readers.CompXKeyboard(product, name); };
        return device;
    }

    static ExtraDevice Shark(Hid.DeviceHit hit)
    {
        ushort vendor = hit.Vendor;
        ushort product = hit.Product;
        string name = Nice(hit.Name, "Mouse");
        var device = new ExtraDevice();
        device.Key = vendor.ToString("X4") + ":" + product.ToString("X4");
        device.Mark = "M";
        device.QuietSeconds = 15 * 60;
        device.Name = name;
        device.Read = delegate { return Readers.AttackSharkMouse(vendor, product, name); };
        return device;
    }

    static ExtraDevice Razer(Hid.DeviceHit hit)
    {
        ushort product = hit.Product;
        string name = product == 0x053A ? "Barracuda Pro" : Nice(hit.Name, "Razer headset");
        var device = new ExtraDevice();
        device.Key = "1532:" + product.ToString("X4");
        device.Mark = "H";
        device.QuietSeconds = 30 * 60;
        device.Name = name;
        device.Read = delegate { return Readers.RazerHeadset(product, name); };
        return device;
    }

    static void AddLogitech(List<ExtraDevice> extras, HashSet<string> seen, List<Hid.DeviceHit> hits)
    {
        var groups = new Dictionary<string, Hid.DeviceHit>();
        foreach (Hid.DeviceHit hit in hits)
        {
            if (hit.Vendor != 0x046D || hit.UsagePage != 0xFF00) continue;
            if (hit.OutputLength != 7 && hit.OutputLength != 20) continue;
            string group = ReceiverKey(hit.Path);
            Hid.DeviceHit current;
            if (!groups.TryGetValue(group, out current) || hit.OutputLength > current.OutputLength)
                groups[group] = hit;
        }
        lock (ProbeGate)
        {
            foreach (KeyValuePair<string, Hid.DeviceHit> pair in groups)
            {
                if (LogitechTried.Contains(pair.Key)) continue;
                LogitechTried.Add(pair.Key);
                foreach (ExtraDevice device in Logitech.Probe(pair.Value))
                    Add(extras, seen, device);
            }
        }
    }

    public static void ForgetLogitech()
    {
        lock (ProbeGate) { LogitechTried.Clear(); }
    }

    static string ReceiverKey(string path)
    {
        if (path == null) return "";
        string text = path.ToLowerInvariant();
        int col = text.IndexOf("&col");
        if (col > 0) text = text.Substring(0, col);
        return text;
    }

    static string Nice(string product, string fallback)
    {
        if (string.IsNullOrEmpty(product)) return fallback;
        if (product.IndexOf("receiver", StringComparison.OrdinalIgnoreCase) >= 0) return fallback;
        if (product.IndexOf("wireless", StringComparison.OrdinalIgnoreCase) >= 0 && product.IndexOf("mouse", StringComparison.OrdinalIgnoreCase) < 0
            && product.IndexOf("keyboard", StringComparison.OrdinalIgnoreCase) < 0 && product.IndexOf("head", StringComparison.OrdinalIgnoreCase) < 0)
            return fallback;
        return product.Length > 40 ? product.Substring(0, 40) : product;
    }
}

internal static class Logitech
{
    public static List<ExtraDevice> Probe(Hid.DeviceHit hit)
    {
        var found = new List<ExtraDevice>();
        int length = hit.OutputLength >= 20 ? 20 : 7;
        IntPtr handle = Hid.Open(hit.Path);
        if (handle == Hid.Invalid) return found;
        try
        {
            for (byte index = 1; index <= 6; index++)
            {
                if (Ask(handle, length, index, 0, 1, null, 250) == null) continue;
                byte unified = Feature(handle, length, index, 0x1004);
                byte older = unified == 0 ? Feature(handle, length, index, 0x1000) : (byte)0;
                if (unified == 0 && older == 0) continue;
                string name = DeviceName(handle, length, index);
                if (string.IsNullOrEmpty(name)) name = "Logitech " + index;
                string mark = Mark(name);
                byte deviceIndex = index;
                ushort product = hit.Product;
                byte which = unified != 0 ? unified : older;
                bool modern = unified != 0;
                int quiet = mark == "H" ? 30 * 60 : mark == "K" ? 10 * 60 : 15 * 60;
                var device = new ExtraDevice();
                device.Key = "046D:" + product.ToString("X4") + ":" + index;
                device.Mark = mark;
                device.QuietSeconds = quiet;
                device.Name = name;
                device.Read = delegate { return Read(product, deviceIndex, which, modern, name); };
                found.Add(device);
            }
        }
        finally { Hid.CloseHandle(handle); }
        return found;
    }

    static DeviceState Read(ushort product, byte index, byte feature, bool modern, string name)
    {
        var state = new DeviceState();
        state.Name = name;
        Hid.DeviceHit hit = Find(product);
        if (hit.Path == null)
        {
            state.Detail = "receiver unplugged";
            return state;
        }
        state.Online = true;
        int length = hit.OutputLength >= 20 ? 20 : 7;
        IntPtr handle = Hid.Open(hit.Path);
        if (handle == Hid.Invalid)
        {
            state.Detail = "receiver busy";
            return state;
        }
        try
        {
            byte function = modern ? (byte)1 : (byte)0;
            byte[] reply = Ask(handle, length, index, feature, function, null, 500);
            if (reply == null || reply.Length < 7)
            {
                state.Detail = "asleep";
                return state;
            }
            if (modern)
            {
                if (reply[4] > 100 || reply[6] >= 5) { state.Detail = "asleep"; return state; }
                state.Percent = reply[4];
                state.Charging = reply[6] == 1 || reply[6] == 2 || reply[6] == 3 || reply[6] == 4;
            }
            else
            {
                int raw = reply[4];
                int[] rough = { 0, 5, 15, 30, 50, 70, 90, 100 };
                if (raw > 7) { state.Detail = "asleep"; return state; }
                state.Percent = rough[raw];
                state.Charging = reply.Length > 6 && (reply[6] == 1 || reply[6] == 2 || reply[6] == 3);
            }
            state.Detail = state.Charging ? "charging" : "wireless";
        }
        finally { Hid.CloseHandle(handle); }
        return state;
    }

    static Hid.DeviceHit Find(ushort product)
    {
        foreach (Hid.DeviceHit hit in Hid.Enumerate())
        {
            if (hit.Vendor == 0x046D && hit.Product == product && hit.UsagePage == 0xFF00
                && (hit.OutputLength == 7 || hit.OutputLength == 20))
                return hit;
        }
        return new Hid.DeviceHit();
    }

    static byte Feature(IntPtr handle, int length, byte index, ushort id)
    {
        byte[] reply = Ask(handle, length, index, 0, 0, new byte[] { (byte)(id >> 8), (byte)id }, 300);
        if (reply == null || reply.Length < 5 || reply[4] == 0) return 0;
        return reply[4];
    }

    static string DeviceName(IntPtr handle, int length, byte index)
    {
        byte feature = Feature(handle, length, index, 0x0005);
        if (feature == 0) return "";
        byte[] info = Ask(handle, length, index, feature, 0, null, 300);
        if (info == null) return "";
        int size = info[4];
        if (size <= 0 || size > 40) size = 16;
        byte[] chunk = Ask(handle, length, index, feature, 1, new byte[] { 0 }, 300);
        if (chunk == null) return "";
        var text = new System.Text.StringBuilder();
        for (int i = 5; i < chunk.Length && text.Length < size; i++)
        {
            if (chunk[i] < 32 || chunk[i] > 126) break;
            text.Append((char)chunk[i]);
        }
        return text.ToString().Trim();
    }

    static string Mark(string name)
    {
        string text = name.ToLowerInvariant();
        if (text.Contains("keyboard") || text.Contains("keys")) return "K";
        if (text.Contains("head") || text.Contains("g733") || text.Contains("g935") || text.Contains("g533") || text.Contains("pro x")) return "H";
        return "M";
    }

    static byte[] Ask(IntPtr handle, int length, byte index, byte feature, byte function, byte[] args, int timeout)
    {
        byte[] packet = new byte[length];
        packet[0] = length > 7 ? (byte)0x11 : (byte)0x10;
        packet[1] = index;
        packet[2] = feature;
        packet[3] = (byte)((function << 4) | 0x0A);
        if (args != null) Buffer.BlockCopy(args, 0, packet, 4, Math.Min(args.Length, packet.Length - 4));
        if (!Hid.Write(handle, packet)) return null;
        foreach (byte[] reply in Hid.ReadUntil(handle, length, timeout))
        {
            if (reply.Length < 5 || reply[1] != index || (reply[3] & 0x0F) != 0x0A) continue;
            if (reply[2] == 0xFF) return null;
            if (reply[2] != feature) continue;
            return reply;
        }
        return null;
    }
}

internal struct SlotSnapshot
{
    public string Key;
    public string Mark;
    public bool Busy;
    public DeviceState State;
}

internal sealed class TaskbarWidget : Form
{
    const int IconSize = 18;
    const int TextGap = 5;
    const int Gap = 14;
    const int Pad = 8;
    static readonly Color Offline = Color.FromArgb(140, 140, 140);
    readonly List<View> views = new List<View>();
    readonly Font font = new Font("Segoe UI Semibold", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font glyphs = IconFont(16f);
    readonly System.Windows.Forms.Timer raise = new System.Windows.Forms.Timer();
    readonly WinEventProc onForeground;
    readonly Action open;
    IntPtr hook;
    bool hovering;

    delegate void WinEventProc(IntPtr hook, uint evt, IntPtr hwnd, int obj, int child, uint thread, uint time);

    [DllImport("user32.dll")]
    static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc proc, uint process, uint thread, uint flags);

    [DllImport("user32.dll")]
    static extern bool UnhookWinEvent(IntPtr hook);

    [DllImport("user32.dll")]
    static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("gdi32.dll")]
    static extern uint GetPixel(IntPtr dc, int x, int y);

    sealed class View
    {
        public string Mark;
        public DeviceState State;
        public string Text;
        public int Width;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr FindWindow(string cls, string name);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string name);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

    [DllImport("user32.dll")]
    static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);

    public TaskbarWidget(Action open)
    {
        this.open = open;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        Cursor = Cursors.Hand;
        BackColor = Color.FromArgb(32, 32, 32);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        onForeground = delegate { Raise(); };
        raise.Interval = 250;
        raise.Tick += delegate { Raise(); };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x80 | 0x08;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        hook = SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, onForeground, 0, 0, 0);
        raise.Start();
    }

    void Raise()
    {
        if (IsHandleCreated) SetWindowPos(Handle, new IntPtr(-1), 0, 0, 0, 0, 0x0001 | 0x0002 | 0x0010);
    }

    public static StringFormat Format(StringAlignment alignment)
    {
        var format = new StringFormat(StringFormat.GenericTypographic);
        format.Alignment = alignment;
        format.LineAlignment = StringAlignment.Center;
        format.FormatFlags |= StringFormatFlags.NoWrap;
        return format;
    }

    public void UpdateDevices(List<SlotSnapshot> slots)
    {
        views.Clear();
        foreach (SlotSnapshot slot in slots)
        {
            var view = new View();
            view.Mark = slot.Mark;
            view.State = slot.State;
            view.Text = slot.State == null || slot.State.Percent == null ? null : slot.State.Percent.Value + "%";
            view.Width = IconSize;
            if (view.Text != null)
            {
                int textWidth = TextRenderer.MeasureText(view.Text, font, new Size(200, 40), TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
                view.Width += TextGap + textWidth + (slot.State.Charging ? 9 : 0);
            }
            views.Add(view);
        }
        views.Sort(delegate(View a, View b) { return Rank(a.Mark).CompareTo(Rank(b.Mark)); });
        PlaceOnTaskbar();
        Render();
    }

    public static int Rank(string mark)
    {
        if (mark == "M") return 0;
        if (mark == "K") return 1;
        if (mark == "H") return 2;
        return 3;
    }

    int ContentWidth()
    {
        int width = 0;
        foreach (View view in views) width += view.Width + Gap;
        if (width == 0) return IconSize;
        return width - Gap;
    }

    void PlaceOnTaskbar()
    {
        int count = Math.Max(views.Count, 1);
        int width = Pad * 2 + ContentWidth();
        int height = 40;
        RECT taskbar;
        IntPtr bar = FindWindow("Shell_TrayWnd", null);
        if (bar == IntPtr.Zero || !GetWindowRect(bar, out taskbar))
        {
            Rectangle work = Screen.PrimaryScreen.WorkingArea;
            MoveWidget(work.Right - width - 12, work.Bottom - height - 8, width, height);
            return;
        }
        int barWidth = taskbar.Right - taskbar.Left;
        int barHeight = taskbar.Bottom - taskbar.Top;
        bool horizontal = barWidth >= barHeight;
        int x;
        int y;
        if (horizontal)
        {
            RECT tray;
            IntPtr trayWindow = FindWindowEx(bar, IntPtr.Zero, "TrayNotifyWnd", null);
            int anchor = trayWindow != IntPtr.Zero && GetWindowRect(trayWindow, out tray) ? tray.Left : taskbar.Right;
            x = anchor - width - 8;
            if (x < taskbar.Left + 60) x = taskbar.Left + 60;
            y = taskbar.Top + Math.Max(0, (barHeight - height) / 2);
        }
        else
        {
            width = Pad * 2 + IconSize + 36;
            height = Pad * 2 + count * 36 - 8;
            x = taskbar.Left < 200 ? taskbar.Right + 8 : taskbar.Left - width - 8;
            y = taskbar.Top + 8;
        }
        MoveWidget(x, y, width, height);
    }

    void MoveWidget(int x, int y, int width, int height)
    {
        if (!IsHandleCreated) return;
        uint flags = 0x0010 | 0x0040;
        if (Location.X == x && Location.Y == y && Width == width && Height == height) flags |= 0x0001 | 0x0002;
        SetWindowPos(Handle, new IntPtr(-1), x, y, width, height, flags);
    }

    void Render()
    {
        if (!IsHandleCreated) return;
        Color behind = TaskbarColor();
        if (behind.ToArgb() != BackColor.ToArgb()) BackColor = behind;
        Invalidate();
    }

    Color TaskbarColor()
    {
        IntPtr screen = GetDC(IntPtr.Zero);
        uint pixel = GetPixel(screen, Left - 3, Top + Height / 2);
        ReleaseDC(IntPtr.Zero, screen);
        if (pixel == 0xFFFFFFFF) return BackColor;
        return Color.FromArgb((int)(pixel & 0xFF), (int)(pixel >> 8 & 0xFF), (int)(pixel >> 16 & 0xFF));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.Clear(BackColor);
        if (hovering)
        {
            using (GraphicsPath path = Round(new RectangleF(1, 4, Width - 2, Height - 8), 5f))
            using (SolidBrush brush = new SolidBrush(Color.FromArgb(26, 255, 255, 255)))
                graphics.FillPath(brush, path);
        }
        int x = Pad;
        foreach (View view in views)
        {
            Color color = view.Text == null ? Offline : GlyphColor(view.Mark);
            const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(graphics, Glyph(view.Mark), glyphs, new Rectangle(x, 0, IconSize, Height), color, flags | TextFormatFlags.HorizontalCenter);
            if (view.Text != null)
            {
                bool low = view.State.Percent.Value <= 15 && !view.State.Charging;
                var textBox = new Rectangle(x + IconSize + TextGap, 0, view.Width, Height);
                TextRenderer.DrawText(graphics, view.Text, font, textBox, low ? Color.FromArgb(255, 96, 96) : Color.White, flags);
                if (view.State.Charging)
                    DrawBolt(graphics, x + view.Width - 8, Height / 2f, color);
            }
            x += view.Width + Gap;
        }
    }

    public static Color GlyphColor(string mark)
    {
        if (mark == "H") return Color.FromArgb(68, 214, 44);
        if (mark == "M") return Color.FromArgb(255, 140, 0);
        return Color.FromArgb(0, 132, 255);
    }

    public static string Glyph(string mark)
    {
        if (mark == "M") return "\uE962";
        if (mark == "H") return "\uE7F6";
        return "\uE765";
    }

    public static Font IconFont(float size)
    {
        foreach (string family in new[] { "Segoe Fluent Icons", "Segoe MDL2 Assets" })
        {
            var candidate = new Font(family, size, FontStyle.Regular, GraphicsUnit.Pixel);
            if (candidate.Name == family) return candidate;
            candidate.Dispose();
        }
        return new Font("Segoe UI Symbol", size, FontStyle.Regular, GraphicsUnit.Pixel);
    }

    public static void DrawBolt(Graphics graphics, float x, float y, Color color)
    {
        PointF[] bolt = new PointF[]
        {
            new PointF(x + 5f, y - 6f),
            new PointF(x + 1f, y + 0.5f),
            new PointF(x + 4f, y + 0.5f),
            new PointF(x + 2f, y + 6f),
            new PointF(x + 7f, y - 0.5f),
            new PointF(x + 4f, y - 0.5f)
        };
        using (SolidBrush brush = new SolidBrush(color))
            graphics.FillPolygon(brush, bolt);
    }

    public static GraphicsPath Round(RectangleF box, float radius)
    {
        float diameter = Math.Min(radius * 2f, Math.Min(box.Width, box.Height));
        var path = new GraphicsPath();
        if (diameter <= 1f)
        {
            path.AddRectangle(box);
            return path;
        }
        path.AddArc(box.X, box.Y, diameter, diameter, 180f, 90f);
        path.AddArc(box.Right - diameter, box.Y, diameter, diameter, 270f, 90f);
        path.AddArc(box.Right - diameter, box.Bottom - diameter, diameter, diameter, 0f, 90f);
        path.AddArc(box.X, box.Bottom - diameter, diameter, diameter, 90f, 90f);
        path.CloseFigure();
        return path;
    }

    protected override void OnMouseEnter(EventArgs e)
    {
        hovering = true;
        Render();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hovering = false;
        Render();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left || e.Button == MouseButtons.Right) open();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (hook != IntPtr.Zero) UnhookWinEvent(hook);
            raise.Dispose();
            font.Dispose();
            glyphs.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class TrayApp : ApplicationContext
{
    const string RunName = "BatteryTray";
    const int MouseQuietSeconds = 90;
    const int KeyboardQuietSeconds = 10 * 60;
    const int HeadphoneQuietSeconds = 30 * 60;
    const int WakeSeconds = 15;
    readonly List<Slot> slots = new List<Slot>();
    readonly System.Windows.Forms.Timer timer;
    readonly object gate = new object();
    readonly Dictionary<string, bool> warned = new Dictionary<string, bool>();
    readonly TaskbarWidget widget;
    readonly Flyout flyout;
    readonly NotifyIcon alerts;
    List<ExtraDevice> pending;
    Thread worker;
    int alertLeft;
    volatile bool running = true;

    sealed class Slot
    {
        public string Key;
        public string Mark;
        public int Quiet;
        public Func<DeviceState> Read;
        public DeviceState State;
        public int Left;
        public bool Busy;
    }

    public TrayApp()
    {
        alerts = new NotifyIcon();
        alerts.Icon = SystemIcons.Information;
        alerts.Visible = false;
        flyout = new Flyout(this);
        widget = new TaskbarWidget(delegate { flyout.Toggle(widget.Bounds); });
        widget.FormClosed += delegate { if (running) ExitThread(); };
        AddSlot("keyboard", "K", KeyboardQuietSeconds, Readers.Keyboard, "Keyboard");
        AddSlot("mouse", "M", MouseQuietSeconds, Readers.Mouse, "Mouse");
        AddSlot("headphones", "H", HeadphoneQuietSeconds, Readers.Headphones, "Headphones");
        widget.Show();
        widget.UpdateDevices(Snapshot());
        Thread setup = new Thread(DongleRecover.Startup);
        setup.IsBackground = true;
        setup.Start();
        worker = new Thread(Loop);
        worker.IsBackground = true;
        worker.Start();
        timer = new System.Windows.Forms.Timer();
        timer.Interval = 1000;
        timer.Tick += delegate
        {
            AdoptPending();
            widget.UpdateDevices(Snapshot());
            if (alertLeft > 0 && --alertLeft == 0) alerts.Visible = false;
        };
        timer.Start();
    }

    void AddSlot(string key, string mark, int quiet, Func<DeviceState> read, string name)
    {
        var slot = new Slot();
        slot.Key = key;
        slot.Mark = mark;
        slot.Quiet = quiet;
        slot.Read = read;
        slot.State = Blank(name);
        slots.Add(slot);
    }

    internal List<SlotSnapshot> Snapshot()
    {
        var list = new List<SlotSnapshot>();
        lock (gate)
        {
            foreach (Slot slot in slots)
            {
                var item = new SlotSnapshot();
                item.Key = slot.Key;
                item.Mark = slot.Mark;
                item.Busy = slot.Busy;
                item.State = slot.State;
                list.Add(item);
            }
        }
        list.Sort(delegate(SlotSnapshot a, SlotSnapshot b) { return TaskbarWidget.Rank(a.Mark).CompareTo(TaskbarWidget.Rank(b.Mark)); });
        return list;
    }

    internal void RefreshDevice(string key)
    {
        Slot target = null;
        lock (gate)
        {
            foreach (Slot slot in slots)
            {
                if (slot.Key == key) target = slot;
            }
        }
        if (target != null) ThreadPool.QueueUserWorkItem(delegate { ReadSlot(target); });
    }

    internal void RefreshAll()
    {
        List<Slot> copy;
        lock (gate) { copy = new List<Slot>(slots); }
        foreach (Slot slot in copy)
        {
            Slot target = slot;
            ThreadPool.QueueUserWorkItem(delegate { ReadSlot(target); });
        }
        ThreadPool.QueueUserWorkItem(delegate
        {
            Catalog.ForgetLogitech();
            List<ExtraDevice> found = Catalog.Discover();
            lock (gate) { pending = found; }
        });
    }

    internal void ReconnectReceiver()
    {
        ThreadPool.QueueUserWorkItem(delegate
        {
            DongleRecover.Recover(true);
            RefreshDevice("headphones");
        });
    }

    void ReadSlot(Slot slot)
    {
        lock (gate)
        {
            if (slot.Busy) return;
            slot.Busy = true;
        }
        try
        {
            Apply(slot, Safe(slot.Read));
            NotifyLow(slot);
        }
        finally
        {
            lock (gate) { slot.Busy = false; }
        }
    }

    static DeviceState Blank(string name)
    {
        var state = new DeviceState();
        state.Name = name;
        state.Detail = "checking…";
        return state;
    }

    void Loop()
    {
        lock (gate) { pending = Catalog.Discover(); }
        while (running)
        {
            List<Slot> due = new List<Slot>();
            lock (gate)
            {
                foreach (Slot slot in slots)
                {
                    if (slot.Left > 0)
                    {
                        slot.Left--;
                        continue;
                    }
                    due.Add(slot);
                }
            }
            foreach (Slot slot in due) ReadSlot(slot);
            if (!running) break;
            Thread.Sleep(1000);
        }
    }

    void AdoptPending()
    {
        List<ExtraDevice> found;
        lock (gate)
        {
            found = pending;
            pending = null;
        }
        if (found == null) return;
        foreach (ExtraDevice extra in found)
        {
            bool exists = false;
            foreach (Slot slot in slots)
            {
                if (slot.Key == extra.Key) exists = true;
            }
            if (exists) continue;
            var added = new Slot();
            added.Key = extra.Key;
            added.Mark = extra.Mark;
            added.Quiet = extra.QuietSeconds;
            added.Read = extra.Read;
            added.State = Blank(extra.Name);
            lock (gate) { slots.Add(added); }
        }
    }

    static DeviceState Safe(Func<DeviceState> read)
    {
        try { return read(); }
        catch (Exception)
        {
            var state = new DeviceState();
            state.Detail = "read failed";
            return state;
        }
    }

    void Apply(Slot slot, DeviceState next)
    {
        bool fresh = next.Percent != null;
        if (fresh) next.Seen = DateTime.Now;
        lock (gate)
        {
            slot.State = KeepLast(slot.State, next);
            slot.Left = fresh ? slot.Quiet : WakeSeconds;
        }
    }

    static DeviceState KeepLast(DeviceState previous, DeviceState next)
    {
        if (next.Percent == null && previous != null && previous.Percent != null && next.Online)
        {
            next.Percent = previous.Percent;
            next.Charging = previous.Charging;
            next.Stale = true;
            next.Seen = previous.Seen;
            next.Detail = previous.Detail;
        }
        if (string.IsNullOrEmpty(next.Name) && previous != null) next.Name = previous.Name;
        return next;
    }

    void NotifyLow(Slot slot)
    {
        DeviceState state = slot.State;
        if (state == null || state.Name == null) return;
        bool low = state.Percent != null && state.Percent.Value <= 15 && !state.Charging;
        bool already;
        lock (gate) { already = warned.ContainsKey(slot.Key) && warned[slot.Key]; }
        if (low && !already)
        {
            lock (gate) { warned[slot.Key] = true; }
            string text = state.Name + " is at " + state.Percent.Value + "%";
            try
            {
                string message = text;
                widget.BeginInvoke(new Action(delegate
                {
                    alerts.Visible = true;
                    alerts.ShowBalloonTip(4000, "Low battery", message, ToolTipIcon.Warning);
                    alertLeft = 8;
                }));
            }
            catch (Exception) { }
        }
        if (!low) lock (gate) { warned[slot.Key] = false; }
    }

    static Icon Draw(int? percent, string mark, out IntPtr handle)
    {
        const int size = 64;
        using (Bitmap bitmap = new Bitmap(size, size))
        using (Graphics graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.Clear(Color.Transparent);

            float thickness = 8f;
            var ring = new RectangleF(4f, 4f, 56f, 56f);
            Color from, mid, to;
            Palette(mark, out from, out mid, out to);
            using (Pen track = new Pen(Color.FromArgb(150, 176, 182, 192), thickness))
                graphics.DrawEllipse(track, ring);

            if (percent != null && percent.Value > 0)
            {
                float sweep = -360f * Math.Min(percent.Value, 100) / 100f;
                if (sweep <= -360f) sweep = -359.5f;
                DrawRing(graphics, ring, -90f, sweep, thickness, from, mid, to);
            }

            handle = bitmap.GetHicon();
            return Icon.FromHandle(handle);
        }
    }

    static void Palette(string mark, out Color from, out Color mid, out Color to)
    {
        if (mark == "H")
        {
            from = Color.FromArgb(168, 255, 70);
            mid = Color.FromArgb(68, 214, 44);
            to = Color.FromArgb(16, 150, 28);
            return;
        }
        if (mark == "M")
        {
            from = Color.FromArgb(255, 214, 30);
            mid = Color.FromArgb(255, 140, 0);
            to = Color.FromArgb(255, 78, 0);
            return;
        }
        from = Color.FromArgb(110, 196, 255);
        mid = Color.FromArgb(0, 132, 255);
        to = Color.FromArgb(0, 72, 214);
    }

    static void DrawRing(Graphics graphics, RectangleF ring, float start, float sweep, float thickness, Color from, Color mid, Color to)
    {
        int steps = Math.Max(16, (int)(Math.Abs(sweep) / 4f));
        float overlap = sweep < 0f ? -1.4f : 1.4f;
        for (int i = 0; i < steps; i++)
        {
            float t = steps == 1 ? 1f : (float)i / (steps - 1);
            using (Pen pen = new Pen(RingColor(t, from, mid, to), thickness))
            {
                pen.StartCap = LineCap.Flat;
                pen.EndCap = LineCap.Flat;
                graphics.DrawArc(pen, ring, start + sweep * i / steps, sweep / steps + overlap);
            }
        }
        if (Math.Abs(sweep) >= 350f) return;
        float radius = thickness / 2f;
        PointF startPoint = OnRing(ring, start);
        PointF endPoint = OnRing(ring, start + sweep);
        using (SolidBrush startBrush = new SolidBrush(RingColor(0f, from, mid, to)))
        using (SolidBrush endBrush = new SolidBrush(RingColor(1f, from, mid, to)))
        {
            graphics.FillEllipse(startBrush, startPoint.X - radius, startPoint.Y - radius, thickness, thickness);
            graphics.FillEllipse(endBrush, endPoint.X - radius, endPoint.Y - radius, thickness, thickness);
        }
    }

    static Color RingColor(float t, Color from, Color mid, Color to)
    {
        if (t < 0.55f) return Blend(from, mid, t / 0.55f);
        return Blend(mid, to, (t - 0.55f) / 0.45f);
    }

    static Color Blend(Color a, Color b, float t)
    {
        return Color.FromArgb(
            (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t),
            (int)(a.B + (b.B - a.B) * t));
    }

    static PointF OnRing(RectangleF ring, float degrees)
    {
        double radians = degrees * Math.PI / 180.0;
        return new PointF(
            ring.X + ring.Width / 2f + ring.Width / 2f * (float)Math.Cos(radians),
            ring.Y + ring.Height / 2f + ring.Height / 2f * (float)Math.Sin(radians));
    }

    internal static bool IsStartup()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
            return key != null && key.GetValue(RunName) != null;
    }

    internal static void SetStartup(bool enabled)
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
        {
            if (key == null) return;
            if (enabled) key.SetValue(RunName, Application.ExecutablePath);
            else key.DeleteValue(RunName, false);
        }
    }

    protected override void ExitThreadCore()
    {
        running = false;
        timer.Stop();
        alerts.Visible = false;
        alerts.Dispose();
        flyout.Dispose();
        widget.Dispose();
        base.ExitThreadCore();
    }
}

internal sealed class Flyout : Form
{
    const int PanelWidth = 340;
    const int HeaderHeight = 52;
    const int RowHeight = 64;
    const int ItemHeight = 40;
    const int Button = 32;
    const string RefreshGlyph = "\uE72C";
    static readonly Color Back = Color.FromArgb(36, 36, 36);
    static readonly Color Card = Color.FromArgb(46, 46, 46);
    static readonly Color Hot = Color.FromArgb(60, 60, 60);
    static readonly Color Line = Color.FromArgb(62, 62, 62);
    static readonly Color Ink = Color.FromArgb(242, 242, 242);
    static readonly Color Muted = Color.FromArgb(160, 160, 160);
    static readonly Color Dim = Color.FromArgb(120, 120, 120);
    static readonly Color Accent = Color.FromArgb(76, 194, 255);
    static readonly Color Low = Color.FromArgb(255, 99, 99);
    const TextFormatFlags LeftText = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis;
    const TextFormatFlags CenterText = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
    readonly TrayApp app;
    readonly Font title = new Font("Segoe UI Semibold", 15f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font name = new Font("Segoe UI Semibold", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font small = new Font("Segoe UI", 12f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font level = new Font("Segoe UI Semibold", 16f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font body = new Font("Segoe UI", 13f, FontStyle.Regular, GraphicsUnit.Pixel);
    readonly Font deviceIcons = TaskbarWidget.IconFont(22f);
    readonly Font icons = TaskbarWidget.IconFont(15f);
    readonly StringFormat centered = TaskbarWidget.Format(StringAlignment.Center);
    readonly System.Windows.Forms.Timer tick = new System.Windows.Forms.Timer();
    readonly List<Hit> hits = new List<Hit>();
    List<SlotSnapshot> devices = new List<SlotSnapshot>();
    string hover;
    float spin;
    bool startup;
    DateTime hiddenAt;

    sealed class Hit
    {
        public string Id;
        public Rectangle Box;
        public Action Run;
    }

    [DllImport("dwmapi.dll")]
    static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public Flyout(TrayApp app)
    {
        this.app = app;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        KeyPreview = true;
        BackColor = Back;
        Width = PanelWidth;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        tick.Interval = 40;
        int frame = 0;
        tick.Tick += delegate
        {
            frame++;
            bool busy = false;
            foreach (SlotSnapshot device in app.Snapshot()) busy |= device.Busy;
            if (busy) spin = (spin + 24f) % 360f;
            else spin = 0f;
            if (busy || frame % 25 == 0) Reload();
        };
    }

    protected override CreateParams CreateParams
    {
        get
        {
            CreateParams parameters = base.CreateParams;
            parameters.ExStyle |= 0x80;
            return parameters;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        int round = 2;
        int border = Line.R | Line.G << 8 | Line.B << 16;
        DwmSetWindowAttribute(Handle, 33, ref round, 4);
        DwmSetWindowAttribute(Handle, 34, ref border, 4);
    }

    public void Toggle(Rectangle anchor)
    {
        if (Visible)
        {
            Hide();
            return;
        }
        if ((DateTime.Now - hiddenAt).TotalMilliseconds < 300) return;
        startup = TrayApp.IsStartup();
        hover = null;
        Reload();
        Rectangle work = Screen.FromRectangle(anchor).WorkingArea;
        int x = Math.Max(work.Left + 12, Math.Min(anchor.Right - Width, work.Right - Width - 12));
        int y = anchor.Top >= work.Bottom - 4 ? work.Bottom - Height - 12 : work.Top + 12;
        Location = new Point(x, y);
        Show();
        Activate();
        tick.Start();
    }

    protected override void OnDeactivate(EventArgs e)
    {
        base.OnDeactivate(e);
        tick.Stop();
        hiddenAt = DateTime.Now;
        Hide();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape) Hide();
    }

    void Reload()
    {
        devices = app.Snapshot();
        hits.Clear();
        Add("all", new Rectangle(PanelWidth - 16 - Button, (HeaderHeight - Button) / 2, Button, Button), app.RefreshAll);
        int y = HeaderHeight;
        foreach (SlotSnapshot device in devices)
        {
            string key = device.Key;
            int buttonY = y + (RowHeight - 6 - Button) / 2;
            Add("device:" + key, new Rectangle(22, buttonY, Button, Button), delegate { app.RefreshDevice(key); });
            y += RowHeight;
        }
        y += 9;
        Add("reconnect", new Rectangle(6, y, PanelWidth - 12, ItemHeight), app.ReconnectReceiver);
        y += ItemHeight;
        Add("startup", new Rectangle(6, y, PanelWidth - 12, ItemHeight), delegate
        {
            startup = !startup;
            TrayApp.SetStartup(startup);
        });
        y += ItemHeight;
        Add("exit", new Rectangle(6, y, PanelWidth - 12, ItemHeight), app.ExitThread);
        y += ItemHeight + 6;
        if (Height != y) Height = y;
        Invalidate();
    }

    void Add(string id, Rectangle box, Action run)
    {
        var hit = new Hit();
        hit.Id = id;
        hit.Box = box;
        hit.Run = run;
        hits.Add(hit);
    }

    Hit Find(string id)
    {
        foreach (Hit hit in hits)
        {
            if (hit.Id == id) return hit;
        }
        return null;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics graphics = e.Graphics;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.Clear(Back);
        bool anyBusy = false;
        foreach (SlotSnapshot device in devices) anyBusy |= device.Busy;

        TextRenderer.DrawText(graphics, "Battery", title, new Rectangle(18, 2, 200, HeaderHeight), Ink, LeftText);
        DrawIconButton(graphics, Find("all"), anyBusy);

        int y = HeaderHeight;
        foreach (SlotSnapshot device in devices)
        {
            DrawDevice(graphics, device, new Rectangle(12, y, PanelWidth - 24, RowHeight - 6));
            y += RowHeight;
        }

        using (Pen pen = new Pen(Line))
            graphics.DrawLine(pen, 0, y + 4, PanelWidth, y + 4);

        DrawItem(graphics, Find("reconnect"), "\uE88E", "Reconnect Razer receiver");
        Hit start = Find("startup");
        DrawItem(graphics, start, "\uE7E8", "Start with Windows");
        DrawSwitch(graphics, new Rectangle(start.Box.Right - 52, start.Box.Y + (start.Box.Height - 20) / 2, 40, 20), startup);
        DrawItem(graphics, Find("exit"), "\uE8BB", "Exit");
    }

    void DrawDevice(Graphics graphics, SlotSnapshot device, Rectangle row)
    {
        DeviceState state = device.State;
        bool online = state.Percent != null;
        using (GraphicsPath path = TaskbarWidget.Round(row, 6f))
        using (SolidBrush brush = new SolidBrush(Card))
            graphics.FillPath(brush, path);

        Hit button = Find("device:" + device.Key);
        DrawIconButton(graphics, button, device.Busy);

        Color tint = online ? TaskbarWidget.GlyphColor(device.Mark) : Dim;
        int iconX = button.Box.Right + 4;
        TextRenderer.DrawText(graphics, TaskbarWidget.Glyph(device.Mark), deviceIcons, new Rectangle(iconX, row.Y, 30, row.Height), tint, CenterText);

        int textLeft = iconX + 34;
        int percentRight = row.Right - 12;
        string percent = online ? state.Percent.Value + "%" : "";
        int percentWidth = online ? TextRenderer.MeasureText(percent, level, new Size(100, 40), LeftText).Width : 0;
        int textWidth = percentRight - percentWidth - 8 - textLeft;

        TextRenderer.DrawText(graphics, state.Name ?? device.Key, name, new Rectangle(textLeft, row.Y + 11, textWidth, 18), Ink, LeftText);
        TextRenderer.DrawText(graphics, Subtitle(state, device.Busy), small, new Rectangle(textLeft, row.Y + 31, textWidth, 16), Muted, LeftText);

        if (online)
        {
            bool low = state.Percent.Value <= 15 && !state.Charging;
            var box = new Rectangle(percentRight - percentWidth, row.Y, percentWidth, row.Height);
            TextRenderer.DrawText(graphics, percent, level, box, low ? Low : Ink, LeftText);
            if (state.Charging) TaskbarWidget.DrawBolt(graphics, box.Left - 11, row.Y + row.Height / 2f, Accent);
        }
    }

    void DrawIconButton(Graphics graphics, Hit hit, bool busy)
    {
        using (GraphicsPath path = TaskbarWidget.Round(hit.Box, hit.Box.Width / 2f))
        using (SolidBrush brush = new SolidBrush(hover == hit.Id ? Color.FromArgb(78, 78, 78) : Color.FromArgb(58, 58, 58)))
            graphics.FillPath(brush, path);
        if (!busy)
        {
            TextRenderer.DrawText(graphics, RefreshGlyph, icons, hit.Box, Ink, CenterText);
            return;
        }
        GraphicsState saved = graphics.Save();
        graphics.TranslateTransform(hit.Box.X + hit.Box.Width / 2f, hit.Box.Y + hit.Box.Height / 2f);
        graphics.RotateTransform(spin);
        using (SolidBrush brush = new SolidBrush(Accent))
            graphics.DrawString(RefreshGlyph, icons, brush, new RectangleF(-Button / 2f, -Button / 2f, Button, Button), centered);
        graphics.Restore(saved);
    }

    void DrawItem(Graphics graphics, Hit hit, string glyph, string label)
    {
        if (hover == hit.Id)
        {
            using (GraphicsPath path = TaskbarWidget.Round(hit.Box, 5f))
            using (SolidBrush brush = new SolidBrush(Hot))
                graphics.FillPath(brush, path);
        }
        TextRenderer.DrawText(graphics, glyph, icons, new Rectangle(hit.Box.X + 10, hit.Box.Y, 24, hit.Box.Height), Ink, CenterText);
        TextRenderer.DrawText(graphics, label, body, new Rectangle(hit.Box.X + 46, hit.Box.Y, hit.Box.Width - 110, hit.Box.Height), Ink, LeftText);
    }

    static void DrawSwitch(Graphics graphics, Rectangle box, bool on)
    {
        using (GraphicsPath track = TaskbarWidget.Round(box, box.Height / 2f))
        {
            if (on)
            {
                using (SolidBrush brush = new SolidBrush(Accent))
                    graphics.FillPath(brush, track);
            }
            else
            {
                using (Pen pen = new Pen(Muted, 1f))
                    graphics.DrawPath(pen, track);
            }
        }
        float knob = on ? 12f : 10f;
        float cx = on ? box.Right - box.Height / 2f : box.Left + box.Height / 2f;
        float cy = box.Top + box.Height / 2f;
        using (SolidBrush brush = new SolidBrush(on ? Color.Black : Muted))
            graphics.FillEllipse(brush, cx - knob / 2f, cy - knob / 2f, knob, knob);
    }

    static string Subtitle(DeviceState state, bool busy)
    {
        if (busy) return "Checking…";
        if (state.Percent == null)
        {
            string detail = state.Detail;
            if (string.IsNullOrEmpty(detail)) return "Not connected";
            if (detail.StartsWith("checking")) return "Checking…";
            return char.ToUpper(detail[0]) + detail.Substring(1);
        }
        if (state.Charging) return "Charging";
        if (state.Stale) return "Asleep · updated " + Ago(state.Seen);
        return "Updated " + Ago(state.Seen);
    }

    static string Ago(DateTime when)
    {
        if (when == default(DateTime)) return "earlier";
        TimeSpan age = DateTime.Now - when;
        if (age.TotalSeconds < 60) return "just now";
        if (age.TotalMinutes < 60) return (int)age.TotalMinutes + " min ago";
        return (int)age.TotalHours + " h ago";
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        string found = null;
        foreach (Hit hit in hits)
        {
            if (hit.Box.Contains(e.Location)) found = hit.Id;
        }
        Cursor = found == null ? Cursors.Default : Cursors.Hand;
        if (found == hover) return;
        hover = found;
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        hover = null;
        Invalidate();
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        foreach (Hit hit in hits)
        {
            if (!hit.Box.Contains(e.Location)) continue;
            if (hit.Id == "exit") Hide();
            hit.Run();
            Reload();
            return;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            tick.Dispose();
            title.Dispose();
            name.Dispose();
            small.Dispose();
            level.Dispose();
            body.Dispose();
            deviceIcons.Dispose();
            icons.Dispose();
            centered.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal static class DongleRecover
{
    const string TaskName = "BatteryTray Razer Dongle";
    const string VidPid = "VID_1532&PID_0552";
    const uint IoctlCyclePort = 0x00220444;
    static bool accessDenied;

    [StructLayout(LayoutKind.Sequential)]
    struct DevInfo
    {
        public int cbSize;
        public Guid ClassGuid;
        public uint DevInst;
        public IntPtr Reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct CyclePort
    {
        public uint ConnectionIndex;
        public uint StatusReturned;
    }

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Locate_DevNode(out uint devInst, string deviceId, uint flags);

    [DllImport("cfgmgr32.dll")]
    static extern int CM_Get_Parent(out uint parent, uint devInst, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_Device_ID(uint devInst, System.Text.StringBuilder buffer, int length, uint flags);

    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode)]
    static extern int CM_Get_DevNode_Registry_Property(uint devInst, uint property, out uint regType, byte[] buffer, ref uint length, uint flags);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceInstanceId(IntPtr set, ref DevInfo info, char[] buffer, int size, out int required);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr SetupDiGetClassDevs(ref Guid guid, string enumerator, IntPtr parent, int flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInfo(IntPtr set, int index, ref DevInfo info);

    [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool SetupDiGetDeviceRegistryProperty(IntPtr set, ref DevInfo info, uint property, out uint type, byte[] buffer, uint size, out uint required);

    [DllImport("setupapi.dll", SetLastError = true)]
    static extern bool SetupDiEnumDeviceInterfaces(IntPtr set, IntPtr info, ref Guid guid, int index, ref Hid.InterfaceData data);

    [DllImport("setupapi.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr set, ref Hid.InterfaceData data, IntPtr detail, int size, out int required, IntPtr info);

    [DllImport("setupapi.dll")]
    static extern bool SetupDiDestroyDeviceInfoList(IntPtr set);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool DeviceIoControl(IntPtr handle, uint code, ref CyclePort input, int inSize, ref CyclePort output, int outSize, out int returned, IntPtr overlapped);

    [DllImport("kernel32.dll")]
    static extern bool CloseHandle(IntPtr handle);

    [DllImport("shell32.dll", CharSet = CharSet.Auto)]
    static extern IntPtr ShellExecute(IntPtr hwnd, string verb, string file, string args, string dir, int show);

    public static void Startup()
    {
        try
        {
            Remember();
            EnsureTask();
        }
        catch (Exception) { }
    }

    public static void Recover(bool force)
    {
        try
        {
            accessDenied = false;
            Remember();
            if (!force && SpeakersPresent()) return;
            Restart();
            if (accessDenied && !IsAdmin())
                ShellExecute(IntPtr.Zero, "runas", Application.ExecutablePath, force ? "--recover-dongle --force" : "--recover-dongle", null, 0);
        }
        catch (Exception) { }
    }

    public static void InstallTask()
    {
        string exe = Application.ExecutablePath;
        string xmlPath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(exe), "dongle-task.xml");
        string xml = "<?xml version=\"1.0\" encoding=\"UTF-16\"?>"
            + "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">"
            + "<Principals><Principal id=\"Author\"><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>"
            + "<Settings><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowHardTerminate>true</AllowHardTerminate><StartWhenAvailable>true</StartWhenAvailable><Enabled>true</Enabled></Settings>"
            + "<Triggers><LogonTrigger><Delay>PT20S</Delay></LogonTrigger></Triggers>"
            + "<Actions><Exec><Command>" + System.Security.SecurityElement.Escape(exe) + "</Command><Arguments>--recover-dongle</Arguments></Exec></Actions>"
            + "</Task>";
        System.IO.File.WriteAllText(xmlPath, xml, System.Text.Encoding.Unicode);
        Run("schtasks.exe", "/Create /F /TN \"" + TaskName + "\" /XML \"" + xmlPath + "\"", 20000);
        try { System.IO.File.Delete(xmlPath); } catch (Exception) { }
    }

    static void EnsureTask()
    {
        if (Run("schtasks.exe", "/Query /TN \"" + TaskName + "\"", 8000) == 0) return;
        ShellExecute(IntPtr.Zero, "runas", Application.ExecutablePath, "--install-dongle-task", null, 0);
    }

    static void Restart()
    {
        string id = CompositeId();
        if (id != null)
        {
            int code = Run("pnputil.exe", "/restart-device \"" + id + "\"", 20000);
            if (code == 5)
            {
                accessDenied = true;
                return;
            }
            Thread.Sleep(4000);
            if (SpeakersPresent()) return;
        }
        if (!CycleSavedPort()) return;
        Thread.Sleep(4000);
    }

    static bool CycleSavedPort()
    {
        string hub;
        int port;
        if (!SavedPort(out hub, out port)) return false;
        string path = HubPath(hub);
        if (path == null) return false;
        IntPtr handle = CreateFile(path, 0xC0000000, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == Hid.Invalid)
        {
            if (Marshal.GetLastWin32Error() == 5) accessDenied = true;
            return false;
        }
        try
        {
            var cycle = new CyclePort();
            cycle.ConnectionIndex = (uint)port;
            int returned;
            bool ok = DeviceIoControl(handle, IoctlCyclePort, ref cycle, 8, ref cycle, 8, out returned, IntPtr.Zero);
            if (!ok && Marshal.GetLastWin32Error() == 5) accessDenied = true;
            return ok;
        }
        finally { CloseHandle(handle); }
    }

    static void Remember()
    {
        string id = CompositeId();
        if (id == null) return;
        uint node;
        if (CM_Locate_DevNode(out node, id, 0) != 0) return;
        uint parent;
        if (CM_Get_Parent(out parent, node, 0) != 0) return;
        var hub = new System.Text.StringBuilder(512);
        if (CM_Get_Device_ID(parent, hub, hub.Capacity, 0) != 0) return;
        uint regType;
        byte[] data = new byte[4];
        uint length = 4;
        if (CM_Get_DevNode_Registry_Property(node, 0x1D, out regType, data, ref length, 0) != 0 || length < 4) return;
        int port = BitConverter.ToInt32(data, 0);
        if (port < 1) return;
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\BatteryTray"))
        {
            if (key == null) return;
            key.SetValue("RazerHub", hub.ToString());
            key.SetValue("RazerPort", port);
        }
    }

    static bool SavedPort(out string hub, out int port)
    {
        hub = null;
        port = 0;
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\BatteryTray", false))
        {
            if (key == null) return false;
            hub = key.GetValue("RazerHub") as string;
            object value = key.GetValue("RazerPort");
            if (hub == null || value == null) return false;
            port = (int)value;
            return port > 0;
        }
    }

    static string CompositeId()
    {
        Guid usb = new Guid("36fc9e60-c465-11cf-8056-444553540000");
        IntPtr set = SetupDiGetClassDevs(ref usb, null, IntPtr.Zero, 2);
        if (set == Hid.Invalid) return null;
        try
        {
            for (int i = 0; ; i++)
            {
                var info = new DevInfo();
                info.cbSize = Marshal.SizeOf(typeof(DevInfo));
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) break;
                char[] buffer = new char[512];
                int required;
                if (!SetupDiGetDeviceInstanceId(set, ref info, buffer, buffer.Length, out required)) continue;
                string id = new string(buffer).TrimEnd('\0');
                if (id.IndexOf(VidPid, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (id.IndexOf("&MI_", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                return id;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    static string HubPath(string hubId)
    {
        string token = hubId.Replace('\\', '#').ToLowerInvariant();
        Guid guid = new Guid("F18A0E88-C30C-11D0-8815-00A0C906BED8");
        IntPtr set = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 0x12);
        if (set == Hid.Invalid) return null;
        try
        {
            for (int i = 0; ; i++)
            {
                var data = new Hid.InterfaceData();
                data.cbSize = Marshal.SizeOf(typeof(Hid.InterfaceData));
                if (!SetupDiEnumDeviceInterfaces(set, IntPtr.Zero, ref guid, i, ref data)) break;
                int required;
                SetupDiGetDeviceInterfaceDetail(set, ref data, IntPtr.Zero, 0, out required, IntPtr.Zero);
                IntPtr detail = Marshal.AllocHGlobal(required);
                try
                {
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                    if (!SetupDiGetDeviceInterfaceDetail(set, ref data, detail, required, out required, IntPtr.Zero)) continue;
                    string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                    if (path != null && path.ToLowerInvariant().Contains(token)) return path;
                }
                finally { Marshal.FreeHGlobal(detail); }
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return null;
    }

    static bool SpeakersPresent()
    {
        Guid audio = new Guid("c166523c-fe0c-4a94-a586-f1a80cfbbf3e");
        IntPtr set = SetupDiGetClassDevs(ref audio, null, IntPtr.Zero, 2);
        if (set == Hid.Invalid) return false;
        try
        {
            for (int i = 0; ; i++)
            {
                var info = new DevInfo();
                info.cbSize = Marshal.SizeOf(typeof(DevInfo));
                if (!SetupDiEnumDeviceInfo(set, i, ref info)) break;
                uint type;
                byte[] buffer = new byte[512];
                uint required;
                if (!SetupDiGetDeviceRegistryProperty(set, ref info, 12, out type, buffer, (uint)buffer.Length, out required)) continue;
                int count = (int)Math.Min(required, (uint)buffer.Length);
                string name = System.Text.Encoding.Unicode.GetString(buffer, 0, count).TrimEnd('\0');
                if (name.IndexOf("Barracuda", StringComparison.OrdinalIgnoreCase) >= 0
                    && name.IndexOf("Speakers", StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
        }
        finally { SetupDiDestroyDeviceInfoList(set); }
        return false;
    }

    static bool IsAdmin()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }

    static int Run(string file, string arguments, int milliseconds)
    {
        var start = new System.Diagnostics.ProcessStartInfo();
        start.FileName = file;
        start.Arguments = arguments;
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        using (System.Diagnostics.Process process = System.Diagnostics.Process.Start(start))
        {
            if (process == null) return 1;
            if (!process.WaitForExit(milliseconds)) return 1;
            return process.ExitCode;
        }
    }
}

internal static class Program
{
    [DllImport("user32.dll", EntryPoint = "SetProcessDPIAware")]
    static extern bool SetProcessDpiAware();

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--install-dongle-task")
        {
            DongleRecover.InstallTask();
            return;
        }
        if (args.Length > 0 && args[0] == "--recover-dongle")
        {
            DongleRecover.Recover(args.Length > 1 && args[1] == "--force");
            return;
        }
        if (args.Length > 0 && args[0] == "--once")
        {
            Print(Readers.Keyboard());
            Print(Readers.Mouse());
            Print(Readers.Headphones());
            foreach (ExtraDevice extra in Catalog.Discover())
                Print(extra.Read());
            return;
        }
        SetProcessDpiAware();
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new TrayApp());
    }

    static void Print(DeviceState state)
    {
        string level = state.Percent == null ? "none" : state.Percent.Value.ToString();
        Console.WriteLine(state.Name + " " + level + " charging=" + state.Charging + " online=" + state.Online + " " + state.Detail);
    }
}
