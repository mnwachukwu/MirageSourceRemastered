namespace Mirage.Shared;

/// <summary>
/// Lays the shipped world down on a machine that has none.
///
/// <para>A package carries its content as <c>seed/</c>, never as <c>data/</c>, so nothing an installer or an
/// update writes can land on top of an authored world. Putting it into play is this one copy, made once.</para>
/// </summary>
public static class SeedDeploy
{
    /// <summary>
    /// Copies <paramref name="seedDir"/> into <paramref name="dataDir"/>, and only when the data directory
    /// does NOT EXIST.
    ///
    /// <para>An existing directory is left alone <b>even when it is empty</b>. An empty data dir is a
    /// deliberate state — a blank world someone means to author into, or one they cleared on purpose — and a
    /// rule that read emptiness as "fresh install" would refill it on the very next launch, which is the one
    /// thing seeding must never do. Presence of the folder is the whole test.</para>
    ///
    /// <para>Staged through a temporary folder and moved into place, so a copy that fails part-way leaves NO
    /// data dir behind. Without that, a half-written world would look "already seeded" forever after.</para>
    ///
    /// <para>Returns how many files were laid down; 0 means there was nothing to do.</para>
    /// </summary>
    public static int SeedIfDataAbsent(string seedDir, string dataDir)
    {
        if (Directory.Exists(dataDir)) return 0;
        if (!Directory.Exists(seedDir)) return 0;

        string staging = dataDir + ".seeding";
        if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);

        int copied = 0;
        try
        {
            foreach (string src in Directory.EnumerateFiles(seedDir, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(staging, Path.GetRelativePath(seedDir, src));
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest);
                copied++;
            }

            // Last possible moment to lose a race with another instance starting at the same time: if one
            // won, its world is the one that stands and this copy is discarded.
            if (Directory.Exists(dataDir))
            {
                Directory.Delete(staging, recursive: true);
                return 0;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dataDir)!);
            Directory.Move(staging, dataDir);
            return copied;
        }
        catch
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); } catch { }
            throw;
        }
    }
}
