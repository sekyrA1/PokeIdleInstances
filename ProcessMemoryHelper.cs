using System.Diagnostics;
using System.Runtime.InteropServices;

namespace PokeIdleMulti;

/// <summary>
/// Utilitário Win32 para reduzir o Working Set (RAM física alocada) do processo atual
/// e de todos os subprocessos WebView2 filhos iniciados por esta aplicação.
/// </summary>
internal static class ProcessMemoryHelper
{
    private const uint Th32csSnapProcess = 0x00000002;
    private static readonly IntPtr InvalidHandleValue = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct ProcessEntry32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szExeFile;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint dwFlags, uint th32ProcessID);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32First(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool Process32Next(IntPtr hSnapshot, ref ProcessEntry32 lppe);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    /// <summary>
    /// Executa coleta de lixo gerenciada e reduz o Working Set do processo principal
    /// e da árvore de subprocessos WebView2 associada a ele.
    /// Retorna a quantidade de processos otimizados.
    /// </summary>
    public static int TrimMemoryTree()
    {
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        GC.WaitForPendingFinalizers();

        int trimmedCount = 0;
        using var current = Process.GetCurrentProcess();
        if (TryEmptyWorkingSet(current))
            trimmedCount++;

        foreach (int pid in GetDescendantProcessIds(current.Id))
        {
            try
            {
                using var child = Process.GetProcessById(pid);
                if (!child.HasExited && TryEmptyWorkingSet(child))
                    trimmedCount++;
            }
            catch
            {
                // Processo filho pode ter encerrado durante a enumeração
            }
        }

        return trimmedCount;
    }

    /// <summary>
    /// Define a prioridade de CPU (ex: BelowNormal ou Idle) para o processo atual
    /// e todos os subprocessos WebView2 filhos.
    /// </summary>
    public static void SetProcessTreePriority(ProcessPriorityClass priority)
    {
        using var current = Process.GetCurrentProcess();
        TrySetPriority(current, priority);

        foreach (int pid in GetDescendantProcessIds(current.Id))
        {
            try
            {
                using var child = Process.GetProcessById(pid);
                if (!child.HasExited)
                    TrySetPriority(child, priority);
            }
            catch
            {
                // Processo filho pode ter encerrado durante a enumeração
            }
        }
    }

    private static void TrySetPriority(Process process, ProcessPriorityClass priority)
    {
        try
        {
            if (process.PriorityClass != priority)
                process.PriorityClass = priority;
        }
        catch
        {
            // Ignora processos sem permissão de ajuste
        }
    }

    private static bool TryEmptyWorkingSet(Process process)
    {
        try
        {
            return EmptyWorkingSet(process.Handle);
        }
        catch
        {
            return false;
        }
    }

    private static List<int> GetDescendantProcessIds(int rootPid)
    {
        var childrenMap = new Dictionary<int, List<int>>();
        IntPtr snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == InvalidHandleValue)
            return [];

        try
        {
            var entry = new ProcessEntry32 { dwSize = (uint)Marshal.SizeOf<ProcessEntry32>() };
            if (Process32First(snapshot, ref entry))
            {
                do
                {
                    int pid = (int)entry.th32ProcessID;
                    int ppid = (int)entry.th32ParentProcessID;
                    if (!childrenMap.TryGetValue(ppid, out var list))
                    {
                        list = [];
                        childrenMap[ppid] = list;
                    }
                    list.Add(pid);
                }
                while (Process32Next(snapshot, ref entry));
            }
        }
        finally
        {
            CloseHandle(snapshot);
        }

        var result = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootPid);

        while (queue.Count > 0)
        {
            int parent = queue.Dequeue();
            if (!childrenMap.TryGetValue(parent, out var kids))
                continue;

            foreach (int kid in kids)
            {
                result.Add(kid);
                queue.Enqueue(kid);
            }
        }

        return result;
    }
}
