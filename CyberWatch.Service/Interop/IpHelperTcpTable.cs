using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace CyberWatch.Service.Interop;

/// <summary>
/// Consulta la tabla TCP IPv4 con PID (equivalente a <c>netstat -ano</c>) vía <c>iphlpapi.dll</c>.
/// </summary>
internal static class IpHelperTcpTable
{
    public const uint AF_INET = 2;
    public const uint TCP_TABLE_OWNER_PID_ALL = 5;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;

    /// <summary>Estado MIB: LISTEN.</summary>
    public const uint MIB_TCP_STATE_LISTEN = 2;

    [DllImport("iphlpapi.dll", SetLastError = true)]
    public static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool bOrder,
        uint ulAf, uint tableClass, uint reserved);

    [StructLayout(LayoutKind.Sequential)]
    public struct MIB_TCPROW_OWNER_PID
    {
        public uint dwState;
        public uint dwLocalAddr;
        public uint dwLocalPort;
        public uint dwRemoteAddr;
        public uint dwRemotePort;
        public uint dwOwningPid;
    }

    /// <summary>
    /// Los campos de puerto en MIB están en orden de red (big-endian en los 16 bits bajos).
    /// </summary>
    public static ushort NetworkOrderPortFieldToHost(uint dword)
    {
        return (ushort)(((dword >> 8) & 0xFF) | ((dword & 0xFF) << 8));
    }

    /// <summary>
    /// <paramref name="dwAddr"/> está en el formato devuelto por la API (orden de red como DWORD).
    /// </summary>
    public static string FormatIpv4(uint dwAddr)
    {
        var bytes = BitConverter.GetBytes(dwAddr);
        if (BitConverter.IsLittleEndian)
            Array.Reverse(bytes);
        return new IPAddress(bytes).ToString();
    }

    /// <summary>
    /// Enumera filas de la tabla extendida TCP IPv4; devuelve lista vacía si falla (best-effort).
    /// </summary>
    public static IReadOnlyList<MIB_TCPROW_OWNER_PID> EnumerarTcp4()
    {
        int bufferSize = 0;
        var ret = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (ret != ERROR_INSUFFICIENT_BUFFER && ret != 0)
            return Array.Empty<MIB_TCPROW_OWNER_PID>();

        var ptr = Marshal.AllocHGlobal(bufferSize);
        try
        {
            ret = GetExtendedTcpTable(ptr, ref bufferSize, true, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
            if (ret != 0)
                return Array.Empty<MIB_TCPROW_OWNER_PID>();

            var numEntries = Marshal.ReadInt32(ptr);
            var rowSize = Marshal.SizeOf<MIB_TCPROW_OWNER_PID>();
            var list = new List<MIB_TCPROW_OWNER_PID>(numEntries);
            var offset = Marshal.SizeOf<uint>(); // dwNumEntries

            for (var i = 0; i < numEntries; i++)
            {
                var rowPtr = IntPtr.Add(ptr, offset + i * rowSize);
                list.Add(Marshal.PtrToStructure<MIB_TCPROW_OWNER_PID>(rowPtr));
            }

            return list;
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
