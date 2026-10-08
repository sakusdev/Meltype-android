// SPDX-License-Identifier: GPL-3.0-or-later

using System.Runtime.InteropServices;
using Meltype.Composition;

namespace Meltype.Android;

/// <summary>
/// Android native Mozc bridge. The native side intentionally exposes the same
/// US/RS record format as the desktop helper, so the managed side does not need
/// Mozc protobuf definitions.
/// </summary>
internal sealed class MozcNativeConverter : IKanjiConverter, ILearningConverter, IDisposable
{
    private const char UnitSeparator = '\x1f';
    private const char RecordSeparator = '\x1e';
    private const int SaveEvery = 5;

    private readonly object _gate = new();
    private readonly Dictionary<string, IReadOnlyList<string>> _candidates = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<(string Reading, IReadOnlyList<string> Candidates)>?> _conversions = new(StringComparer.Ordinal);
    private IntPtr _handle;
    private int _learnedSinceSave;

    private MozcNativeConverter(IntPtr handle)
    {
        _handle = handle;
    }

    public static MozcNativeConverter? TryCreate(string profileDirectory, string dataFilePath)
    {
        try
        {
            Directory.CreateDirectory(profileDirectory);
            var handle = Native.Create(profileDirectory, dataFilePath);
            return handle == IntPtr.Zero ? null : new MozcNativeConverter(handle);
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    public string? Convert(string hiragana) =>
        ConvertClauses(hiragana) is { Count: > 0 } clauses
            ? string.Concat(clauses.Select(c => c.Text))
            : null;

    public IReadOnlyList<ConversionClause>? ConvertClauses(string hiragana, string? context = null)
    {
        if (Request(context ?? "", hiragana) is not { Count: > 0 } segments)
            return null;

        return segments
            .Select(s => new ConversionClause(s.Reading, s.Candidates.FirstOrDefault() ?? s.Reading))
            .ToList();
    }

    public IReadOnlyList<string> Candidates(string reading)
    {
        lock (_gate)
        {
            if (_candidates.TryGetValue(reading, out var cached))
                return cached;
        }

        var segments = Request("", reading);
        if (segments is null)
            return [];

        return segments.Count == 1
            ? segments[0].Candidates
            : [string.Concat(segments.Select(s => s.Candidates.FirstOrDefault() ?? s.Reading))];
    }

    public void Learn(string? context, IReadOnlyList<ConversionClause> clauses)
    {
        if (clauses.Count == 0 || _handle == IntPtr.Zero)
            return;

        var records = string.Join(RecordSeparator, clauses.Select(c =>
            Clean(c.Reading).Replace(UnitSeparator, ' ') + UnitSeparator +
            Clean(c.Text).Replace(UnitSeparator, ' ')));

        lock (_gate)
        {
            _candidates.Clear();
            _conversions.Clear();
            if (Native.Learn(_handle, Clean(context ?? ""), records) != 0 &&
                ++_learnedSinceSave >= SaveEvery)
            {
                _learnedSinceSave = 0;
                Native.Save(_handle);
            }
        }
    }

    private List<(string Reading, IReadOnlyList<string> Candidates)>? Request(string context, string reading)
    {
        if (string.IsNullOrEmpty(reading) || _handle == IntPtr.Zero)
            return null;

        context = Clean(context);
        reading = Clean(reading);
        lock (_gate)
        {
            var key = context + "\t" + reading;
            if (_conversions.TryGetValue(key, out var known))
            {
                foreach (var (segmentReading, candidates) in known ?? [])
                    _candidates[segmentReading] = candidates;
                return known;
            }

            var pointer = Native.Convert(_handle, context, reading);
            if (pointer == IntPtr.Zero)
                return Remember(key, null);

            string line;
            try
            {
                line = Marshal.PtrToStringUTF8(pointer) ?? "";
            }
            finally
            {
                Native.FreeString(pointer);
            }

            if (line.Length == 0)
                return Remember(key, null);

            var segments = new List<(string, IReadOnlyList<string>)>();
            foreach (var record in line.Split(RecordSeparator))
            {
                var fields = record.Split(UnitSeparator);
                if (fields.Length == 0 || fields[0].Length == 0)
                    continue;

                var candidates = fields.Skip(1)
                    .Where(c => c.Length > 0)
                    .Distinct()
                    .ToList();
                segments.Add((fields[0], candidates));
                if (_candidates.Count > 512)
                    _candidates.Clear();
                _candidates[fields[0]] = candidates;
            }

            return Remember(key, segments);
        }
    }

    private List<(string Reading, IReadOnlyList<string> Candidates)>? Remember(
        string key,
        List<(string Reading, IReadOnlyList<string> Candidates)>? segments)
    {
        if (_conversions.Count > 512)
            _conversions.Clear();
        _conversions[key] = segments;
        return segments;
    }

    private static string Clean(string text) =>
        text.Replace('\t', ' ').Replace('\n', ' ').Replace('\r', ' ');

    public void Dispose()
    {
        lock (_gate)
        {
            if (_handle == IntPtr.Zero)
                return;
            Native.Save(_handle);
            Native.Destroy(_handle);
            _handle = IntPtr.Zero;
            _candidates.Clear();
            _conversions.Clear();
        }
    }

    private static class Native
    {
        private const string Library = "meltype_mozc";

        [DllImport(Library, EntryPoint = "meltype_mozc_create", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Create(
            [MarshalAs(UnmanagedType.LPUTF8Str)] string profileDirectory,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string dataFilePath);

        [DllImport(Library, EntryPoint = "meltype_mozc_destroy", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void Destroy(IntPtr handle);

        [DllImport(Library, EntryPoint = "meltype_mozc_convert", CallingConvention = CallingConvention.Cdecl)]
        internal static extern IntPtr Convert(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string context,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string reading);

        [DllImport(Library, EntryPoint = "meltype_mozc_learn", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Learn(
            IntPtr handle,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string context,
            [MarshalAs(UnmanagedType.LPUTF8Str)] string clauses);

        [DllImport(Library, EntryPoint = "meltype_mozc_save", CallingConvention = CallingConvention.Cdecl)]
        internal static extern int Save(IntPtr handle);

        [DllImport(Library, EntryPoint = "meltype_mozc_free_string", CallingConvention = CallingConvention.Cdecl)]
        internal static extern void FreeString(IntPtr value);
    }
}
