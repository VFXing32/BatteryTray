using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
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

internal sealed class TrayApp : ApplicationContext
{
    const string RunName = "BatteryTray";
    const int MouseQuietSeconds = 15 * 60;
    const int KeyboardQuietSeconds = 10 * 60;
    const int HeadphoneQuietSeconds = 30 * 60;
    readonly List<Slot> slots = new List<Slot>();
    readonly System.Windows.Forms.Timer timer;
    readonly object gate = new object();
    readonly Dictionary<string, bool> warned = new Dictionary<string, bool>();
    List<ExtraDevice> pending;
    Thread worker;
    volatile bool running = true;

    sealed class Slot
    {
        public string Key;
        public string Mark;
        public int Quiet;
        public Func<DeviceState> Read;
        public NotifyIcon Icon;
        public IntPtr Handle;
        public DeviceState State;
        public int Left;
    }

    public TrayApp()
    {
        AddSlot("keyboard", "K", KeyboardQuietSeconds, Readers.Keyboard, "Keyboard");
        AddSlot("mouse", "M", MouseQuietSeconds, Readers.Mouse, "Mouse");
        AddSlot("headphones", "H", HeadphoneQuietSeconds, Readers.Headphones, "Headphones");
        ApplyIcons();
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
            ApplyIcons();
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
        slot.Icon = MakeIcon(name);
        slots.Add(slot);
    }

    NotifyIcon MakeIcon(string name)
    {
        var icon = new NotifyIcon();
        icon.Visible = true;
        icon.Text = name;
        icon.ContextMenuStrip = BuildMenu();
        icon.MouseUp += OnMouseUp;
        return icon;
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
                    slot.Left = slot.Quiet;
                }
            }
            foreach (Slot slot in due)
            {
                DeviceState next = Safe(slot.Read);
                lock (gate) { slot.State = KeepLast(slot.State, next); }
                NotifyLow(slot);
            }
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
            added.Icon = MakeIcon(extra.Name);
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

    static DeviceState KeepLast(DeviceState previous, DeviceState next)
    {
        if (next.Percent == null && previous != null && previous.Percent != null && next.Online)
        {
            next.Percent = previous.Percent;
            next.Charging = previous.Charging;
            if (next.Detail == null || next.Detail.StartsWith("asleep") || next.Detail.StartsWith("no report"))
                next.Detail = previous.Detail + ", last reading";
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
            try { if (slot.Icon != null) slot.Icon.ShowBalloonTip(4000, "Low battery", text, ToolTipIcon.Warning); }
            catch (Exception) { }
        }
        if (!low) lock (gate) { warned[slot.Key] = false; }
    }

    void ApplyIcons()
    {
        List<Slot> copy;
        lock (gate) { copy = new List<Slot>(slots); }
        foreach (Slot slot in copy)
            slot.Handle = Paint(slot.Icon, slot.State, slot.Mark, slot.Handle);
    }

    static IntPtr Paint(NotifyIcon tray, DeviceState state, string mark, IntPtr previousHandle)
    {
        string tip = Line(state);
        if (tip.Length > 63) tip = tip.Substring(0, 63);
        tray.Text = tip;
        IntPtr created;
        Icon icon = Draw(state.Percent, mark, out created);
        Icon previous = tray.Icon;
        tray.Icon = icon;
        if (previous != null) previous.Dispose();
        if (previousHandle != IntPtr.Zero) DestroyIcon(previousHandle);
        ContextMenuStrip menu = tray.ContextMenuStrip;
        if (menu != null && menu.Items.Count > 0)
            menu.Items[0].Text = Line(state);
        return created;
    }

    [DllImport("user32.dll", SetLastError = true)]
    static extern bool DestroyIcon(IntPtr handle);

    static string Line(DeviceState state)
    {
        string level = state.Percent == null ? "—" : state.Percent.Value + "%" + (state.Charging ? " charging" : "");
        string detail = string.IsNullOrEmpty(state.Detail) ? "" : "  " + state.Detail;
        return state.Name + "   " + level + detail;
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

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Checking…").Enabled = false;
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Refresh now", null, delegate { ThreadPool.QueueUserWorkItem(delegate { RefreshNow(); }); });
        menu.Items.Add("Reconnect Razer receiver", null, delegate { ThreadPool.QueueUserWorkItem(delegate { DongleRecover.Recover(true); }); });
        var startup = new ToolStripMenuItem("Start with Windows");
        startup.Checked = IsStartup();
        startup.Click += delegate
        {
            startup.Checked = !startup.Checked;
            SetStartup(startup.Checked);
        };
        menu.Items.Add(startup);
        menu.Items.Add("Exit", null, delegate { ExitThread(); });
        return menu;
    }

    void RefreshNow()
    {
        List<Slot> copy;
        lock (gate) { copy = new List<Slot>(slots); }
        foreach (Slot slot in copy)
        {
            DeviceState next = Safe(slot.Read);
            lock (gate)
            {
                slot.State = KeepLast(slot.State, next);
                slot.Left = slot.Quiet;
            }
        }
        Catalog.ForgetLogitech();
        lock (gate) { pending = Catalog.Discover(); }
    }

    void OnMouseUp(object sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        NotifyIcon icon = sender as NotifyIcon;
        if (icon != null) MethodInfoShow(icon.ContextMenuStrip);
    }

    static void MethodInfoShow(ContextMenuStrip menu)
    {
        if (menu == null) return;
        menu.Show(Cursor.Position);
    }

    static bool IsStartup()
    {
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
            return key != null && key.GetValue(RunName) != null;
    }

    static void SetStartup(bool enabled)
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
        foreach (Slot slot in slots)
            CloseIcon(slot.Icon, slot.Handle);
        base.ExitThreadCore();
    }

    static void CloseIcon(NotifyIcon icon, IntPtr handle)
    {
        icon.Visible = false;
        icon.Dispose();
        if (handle != IntPtr.Zero) DestroyIcon(handle);
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
