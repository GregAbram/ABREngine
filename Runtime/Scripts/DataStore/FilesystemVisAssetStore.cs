/* FilesystemVisAssetStore.cs
 *
 * Copyright (c) 2026 University of Minnesota
 *
 * This program is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * This program is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with this program.  If not, see <https://www.gnu.org/licenses/>.
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Persists VisAssets to disk using exactly the same layout ABREngine's
    /// VisAssetManager and abr_server's visasset_manager.py already use:
    /// "&lt;root&gt;/&lt;uuid&gt;/artifact.json" plus every other file the
    /// VisAsset references, alongside it in that same "&lt;root&gt;/&lt;uuid&gt;/"
    /// folder -- so stored data stays inspectable/compatible the same way
    /// it is today.
    /// </summary>
    public class FilesystemVisAssetStore : ILocalVisAssetStore
    {
        public const string ArtifactJsonFileName = "artifact.json";

        private readonly string root;
        private readonly object fileLock = new object();

        public FilesystemVisAssetStore(string root)
        {
            this.root = root;
            Directory.CreateDirectory(root);
        }

        private string VisAssetDir(string uuid)
        {
            return Path.Combine(root, uuid);
        }

        private string ArtifactJsonFile(string uuid)
        {
            return Path.Combine(VisAssetDir(uuid), ArtifactJsonFileName);
        }

        public bool HasVisAsset(string uuid)
        {
            lock (fileLock)
            {
                return File.Exists(ArtifactJsonFile(uuid));
            }
        }

        public void SaveVisAsset(string uuid, string json, IReadOnlyDictionary<string, byte[]> files)
        {
            lock (fileLock)
            {
                string dir = VisAssetDir(uuid);
                Directory.CreateDirectory(dir);
                File.WriteAllText(ArtifactJsonFile(uuid), json);
                foreach (KeyValuePair<string, byte[]> file in files)
                {
                    File.WriteAllBytes(Path.Combine(dir, file.Key), file.Value);
                }
            }
        }

        public bool TryGetVisAsset(string uuid, out VisAssetEntry entry)
        {
            lock (fileLock)
            {
                string artifactJsonFile = ArtifactJsonFile(uuid);
                if (!File.Exists(artifactJsonFile))
                {
                    entry = null;
                    return false;
                }

                entry = new VisAssetEntry
                {
                    Uuid = uuid,
                    Json = File.ReadAllText(artifactJsonFile)
                };

                string dir = VisAssetDir(uuid);
                foreach (string filePath in Directory.EnumerateFiles(dir))
                {
                    string fileName = Path.GetFileName(filePath);
                    if (fileName == ArtifactJsonFileName)
                    {
                        continue;
                    }
                    entry.Files[fileName] = File.ReadAllBytes(filePath);
                }

                return true;
            }
        }

        public IReadOnlyCollection<VisAssetSummary> ListVisAssetSummaries()
        {
            lock (fileLock)
            {
                if (!Directory.Exists(root))
                {
                    return new List<VisAssetSummary>();
                }
                return Directory.EnumerateDirectories(root)
                    .Select(dir => Path.Combine(dir, ArtifactJsonFileName))
                    .Where(File.Exists)
                    .Select(artifactJsonFile => new VisAssetSummary
                    {
                        Uuid = Path.GetFileName(Path.GetDirectoryName(artifactJsonFile)),
                        Json = File.ReadAllText(artifactJsonFile)
                    })
                    .ToList();
            }
        }
    }
}
