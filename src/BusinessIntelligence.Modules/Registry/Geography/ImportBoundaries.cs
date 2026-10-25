using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using BusinessIntelligence.Modules.Platform.Files;
using BusinessIntelligence.Modules.Platform.Persistence;
using BusinessIntelligence.Modules.Registry.Sources;
using Microsoft.EntityFrameworkCore;

namespace BusinessIntelligence.Modules.Registry.Geography;

/// <summary>
/// An administrative area's outline as an SVG path in one national frame (<see cref="ImportBoundaries.Width"/> wide,
/// north up), simplified for display. Code is the SIRUTA code (counties: the county's SIRUTA code).
/// </summary>
public sealed class AreaOutline
{
    public int Code { get; set; }

    /// <summary>1 for counties, 2 for UATs.</summary>
    public short Level { get; set; }
    public required string Name { get; set; }
    public required string Path { get; set; }
    public int SourceFileId { get; set; }
}

/// <summary>
/// Imports ANCPI administrative boundaries (shapefile zips, Stereo70 metres) as simplified SVG outlines. One file per
/// level; a newer file for the same level replaces it.
/// </summary>
public sealed class ImportBoundaries(AppDbContext database, TimeProvider time)
{
    public const string Kind = "boundaries-ancpi";
    public const string ParserVersion = "ancpi-shapefile-v2";

    /// <summary>The national frame: Stereo70 extent of Romania mapped to this many SVG units across.</summary>
    public const double Width = 1000;
    public const double MinX = 120_000, MaxX = 885_000, MinY = 220_000, MaxY = 770_000;
    public static double Height => Width * (MaxY - MinY) / (MaxX - MinX);

    /// <summary>
    /// Douglas–Peucker tolerance in metres: counties are drawn as the national backdrop (coarse, about 2 SVG units), UATs
    /// as the highlighted area (about half a unit).
    /// </summary>
    public static double Tolerance(short level) => level == 1 ? 1500 : 350;

    public async Task<ImportResult> ExecuteAsync(string path, CatalogFile file, CancellationToken cancellationToken)
    {
        if (file.Kind != Kind)
        {
            throw new ArgumentException($"Catalog entry {file.Id} is not an ANCPI boundary file.", nameof(file));
        }

        var existing = await database.SourceFiles.AsNoTracking()
            .FirstOrDefaultAsync(item => item.Sha256 == file.Sha256 && item.ParserVersion == ParserVersion, cancellationToken);
        if (existing is not null)
        {
            return new ImportResult(ImportOutcome.AlreadyImported, existing.Id, new Dictionary<string, object>());
        }

        await using (var verified = VerifiedFileStream.Open(path, file))
        {
            verified.EnsureComplete();
        }

        using var archive = ZipFile.OpenRead(path);
        var shp = Entry(archive, ".shp");
        var dbf = Entry(archive, ".dbf");
        List<(int Row, Dictionary<string, string> Fields)> records;
        using (var stream = dbf.Open())
        {
            records = [.. Dbf.Read(stream, Encoding.UTF8)];
        }

        var shapes = new List<List<List<(double X, double Y)>>>();
        using (var stream = shp.Open())
        {
            shapes.AddRange(Polygons(stream));
        }

        if (shapes.Count != records.Count)
        {
            throw new InvalidDataException($"{file.FileName}: {shapes.Count} shapes but {records.Count} attribute records.");
        }

        var level = records.FirstOrDefault().Fields?.GetValueOrDefault("natLevel") switch
        {
            "2ndOrder" => (short)1,
            "3rdOrder" => (short)2,
            var other => throw new InvalidDataException($"{file.FileName}: unexpected natLevel {other}."),
        };

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
        var source = RegistryImports.NewSourceFile(file, ParserVersion, time, "{}");
        database.SourceFiles.Add(source);
        await database.SaveChangesAsync(cancellationToken);
        await database.AreaOutlines.Where(item => item.Level == level).ExecuteDeleteAsync(cancellationToken);
        var points = 0;
        for (var index = 0; index < records.Count; index++)
        {
            var fields = records[index].Fields;
            if (!int.TryParse(fields.GetValueOrDefault("natCode"), NumberStyles.None, CultureInfo.InvariantCulture, out var code))
            {
                throw new InvalidDataException($"{file.FileName}: record {index + 1} has no SIRUTA code.");
            }

            var svg = Svg(shapes[index], Tolerance(level), out var kept);
            points += kept;
            database.AreaOutlines.Add(new AreaOutline { Code = code, Level = level, Name = fields.GetValueOrDefault("name") ?? code.ToString(CultureInfo.InvariantCulture), Path = svg, SourceFileId = source.Id });
        }

        var report = new Dictionary<string, object> { ["areas"] = records.Count, ["level"] = level, ["pointsKept"] = points };
        source.Report = JsonSerializer.Serialize(report);
        await database.SaveChangesAsync(cancellationToken);
        await RegistryImports.PublishAsync(database, $"{Kind}-{level}", source.Id, time, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ImportResult(ImportOutcome.Published, source.Id, report);
    }

    private static ZipArchiveEntry Entry(ZipArchive archive, string extension) =>
        archive.Entries.SingleOrDefault(entry => entry.FullName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidDataException($"The shapefile zip has no {extension} file.");

    /// <summary>Polygon records (shape type 5) of a .shp stream, as rings of points.</summary>
    public static IEnumerable<List<List<(double X, double Y)>>> Polygons(Stream stream)
    {
        using var reader = new BinaryReader(stream);
        var header = reader.ReadBytes(100);
        if (header.Length < 100 || BitConverter.ToInt32(header, 32) != 5)
        {
            throw new InvalidDataException("Not a polygon shapefile.");
        }

        while (true)
        {
            var recordHeader = reader.ReadBytes(8);
            if (recordHeader.Length < 8)
            {
                yield break;
            }

            var length = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(recordHeader.AsSpan(4)) * 2;
            var content = reader.ReadBytes(length);
            if (content.Length < length || BitConverter.ToInt32(content, 0) is var type && type == 0)
            {
                yield return [];
                continue;
            }

            var parts = BitConverter.ToInt32(content, 36);
            var count = BitConverter.ToInt32(content, 40);
            var starts = Enumerable.Range(0, parts).Select(part => BitConverter.ToInt32(content, 44 + part * 4)).Append(count).ToArray();
            var origin = 44 + parts * 4;
            var rings = new List<List<(double X, double Y)>>();
            for (var part = 0; part < parts; part++)
            {
                var ring = new List<(double X, double Y)>(starts[part + 1] - starts[part]);
                for (var point = starts[part]; point < starts[part + 1]; point++)
                {
                    ring.Add((BitConverter.ToDouble(content, origin + point * 16), BitConverter.ToDouble(content, origin + point * 16 + 8)));
                }

                rings.Add(ring);
            }

            yield return rings;
        }
    }

    /// <summary>Simplified rings as an SVG path in the national frame; rings that collapse below a few points are dropped.</summary>
    public static string Svg(List<List<(double X, double Y)>> rings, double tolerance, out int points)
    {
        var scale = Width / (MaxX - MinX);
        var builder = new StringBuilder();
        points = 0;
        foreach (var ring in rings)
        {
            var simple = Simplify(ring, tolerance);
            if (simple.Count < 4)
            {
                continue;
            }

            for (var index = 0; index < simple.Count - 1; index++)
            {
                var (x, y) = simple[index];
                builder.Append(index == 0 ? 'M' : 'L')
                    .Append(((x - MinX) * scale).ToString("0.#", CultureInfo.InvariantCulture)).Append(',')
                    .Append(((MaxY - y) * scale).ToString("0.#", CultureInfo.InvariantCulture));
            }

            builder.Append('Z');
            points += simple.Count - 1;
        }

        return builder.ToString();
    }

    private static List<(double X, double Y)> Simplify(List<(double X, double Y)> points, double tolerance)
    {
        if (points.Count < 4)
        {
            return points;
        }

        var keep = new bool[points.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int First, int Last)>();
        stack.Push((0, points.Count - 1));
        while (stack.Count > 0)
        {
            var (first, last) = stack.Pop();
            var (farthest, distance) = (-1, 0.0);
            for (var index = first + 1; index < last; index++)
            {
                var d = Distance(points[index], points[first], points[last]);
                if (d > distance)
                {
                    (farthest, distance) = (index, d);
                }
            }

            if (farthest > 0 && distance > tolerance)
            {
                keep[farthest] = true;
                stack.Push((first, farthest));
                stack.Push((farthest, last));
            }
        }

        // A closed ring's first and last points coincide: keep an extra point so the ring is not reduced to a line.
        if (keep.Count(flag => flag) < 4)
        {
            keep[points.Count / 3] = keep[2 * points.Count / 3] = true;
        }

        return [.. points.Where((_, index) => keep[index])];
    }

    private static double Distance((double X, double Y) point, (double X, double Y) start, (double X, double Y) end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        var length = dx * dx + dy * dy;
        if (length == 0)
        {
            return Math.Sqrt(Math.Pow(point.X - start.X, 2) + Math.Pow(point.Y - start.Y, 2));
        }

        var t = Math.Clamp(((point.X - start.X) * dx + (point.Y - start.Y) * dy) / length, 0, 1);
        return Math.Sqrt(Math.Pow(point.X - (start.X + t * dx), 2) + Math.Pow(point.Y - (start.Y + t * dy), 2));
    }
}
