/* FilesystemProjectStore.cs
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
    /// Persists projects to disk using exactly the same layout ABREngine's
    /// DataManager/ProjectInfo already use: "&lt;root&gt;/&lt;Org&gt;/&lt;Dataset&gt;/project.json"
    /// and "&lt;root&gt;/&lt;Org&gt;/&lt;Dataset&gt;/KeyData/&lt;Name&gt;.json"+".bin", so stored
    /// data stays inspectable/compatible the same way it is today. Payloads
    /// are stored verbatim as opaque text/bytes -- never parsed.
    /// </summary>
    public class FilesystemProjectStore : ILocalProjectStore
    {
        private const string ProjectInfoFileName = "project.json";
        private const string KeyDataDirName = "KeyData";
        private const string StateDirName = "State";

        private readonly string root;
        private readonly object fileLock = new object();

        public FilesystemProjectStore(string root)
        {
            this.root = root;
            Directory.CreateDirectory(root);
        }

        private string ProjectDir(string projectPath)
        {
            return Path.Combine(root, projectPath.Replace(ProjectPaths.Separator, Path.DirectorySeparatorChar));
        }

        private string ProjectInfoFile(string projectPath)
        {
            return Path.Combine(ProjectDir(projectPath), ProjectInfoFileName);
        }

        private string KeyDataDir(string projectPath)
        {
            return Path.Combine(ProjectDir(projectPath), KeyDataDirName);
        }

        private string StateDir(string projectPath)
        {
            return Path.Combine(ProjectDir(projectPath), StateDirName);
        }

        public bool HasProject(string projectPath)
        {
            lock (fileLock)
            {
                if (File.Exists(ProjectInfoFile(projectPath)))
                {
                    return true;
                }
                string keyDataDir = KeyDataDir(projectPath);
                return Directory.Exists(keyDataDir) && Directory.EnumerateFiles(keyDataDir, "*.json").Any();
            }
        }

        public void SaveProjectInfo(string projectPath, string json)
        {
            lock (fileLock)
            {
                Directory.CreateDirectory(ProjectDir(projectPath));
                File.WriteAllText(ProjectInfoFile(projectPath), json);
            }
        }

        public void SaveKeyData(string keyDataPath, string json, byte[] binData)
        {
            string projectPath = ProjectPaths.GetProjectPath(keyDataPath);
            string name = ProjectPaths.GetName(keyDataPath);

            lock (fileLock)
            {
                string keyDataDir = KeyDataDir(projectPath);
                Directory.CreateDirectory(keyDataDir);
                File.WriteAllText(Path.Combine(keyDataDir, name + ".json"), json);
                if (binData != null && binData.Length > 0)
                {
                    File.WriteAllBytes(Path.Combine(keyDataDir, name + ".bin"), binData);
                }
            }
        }

        public void SaveState(string statePath, string json)
        {
            string projectPath = ProjectPaths.GetProjectPath(statePath);
            string name = ProjectPaths.GetName(statePath);

            lock (fileLock)
            {
                string stateDir = StateDir(projectPath);
                Directory.CreateDirectory(stateDir);
                File.WriteAllText(Path.Combine(stateDir, name + ".json"), json);
            }
        }

        public IReadOnlyCollection<string> ListStateNames(string projectPath)
        {
            lock (fileLock)
            {
                string stateDir = StateDir(projectPath);
                if (!Directory.Exists(stateDir))
                {
                    return new List<string>();
                }
                return Directory.EnumerateFiles(stateDir, "*.json")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToList();
            }
        }

        public IReadOnlyCollection<KeyDataSummary> ListKeyDataSummaries(string projectPath)
        {
            lock (fileLock)
            {
                string keyDataDir = KeyDataDir(projectPath);
                if (!Directory.Exists(keyDataDir))
                {
                    return new List<KeyDataSummary>();
                }
                return Directory.EnumerateFiles(keyDataDir, "*.json")
                    .Select(f => new KeyDataSummary
                    {
                        Name = Path.GetFileNameWithoutExtension(f),
                        Json = File.ReadAllText(f)
                    })
                    .ToList();
            }
        }

        public IReadOnlyCollection<string> ListLocalProjects()
        {
            var projects = new List<string>();
            lock (fileLock)
            {
                if (!Directory.Exists(root))
                {
                    return projects;
                }

                foreach (string orgDir in Directory.EnumerateDirectories(root))
                {
                    string org = Path.GetFileName(orgDir);
                    foreach (string datasetDir in Directory.EnumerateDirectories(orgDir))
                    {
                        string dataset = Path.GetFileName(datasetDir);
                        string projectPath = org + ProjectPaths.Separator + dataset;
                        if (HasProject(projectPath))
                        {
                            projects.Add(projectPath);
                        }
                    }
                }
            }
            return projects;
        }

        public bool TryReplayProject(string projectPath, out ProjectSnapshot snapshot)
        {
            lock (fileLock)
            {
                if (!HasProject(projectPath))
                {
                    snapshot = null;
                    return false;
                }

                snapshot = new ProjectSnapshot { ProjectPath = projectPath };

                string projectInfoFile = ProjectInfoFile(projectPath);
                if (File.Exists(projectInfoFile))
                {
                    snapshot.ProjectInfoJson = File.ReadAllText(projectInfoFile);
                }

                string keyDataDir = KeyDataDir(projectPath);
                if (Directory.Exists(keyDataDir))
                {
                    foreach (string jsonFile in Directory.EnumerateFiles(keyDataDir, "*.json"))
                    {
                        string name = Path.GetFileNameWithoutExtension(jsonFile);
                        string binFile = Path.Combine(keyDataDir, name + ".bin");
                        snapshot.KeyData.Add(new KeyDataEntry
                        {
                            KeyDataPath = projectPath + ProjectPaths.Separator + KeyDataDirName + ProjectPaths.Separator + name,
                            Json = File.ReadAllText(jsonFile),
                            BinData = File.Exists(binFile) ? File.ReadAllBytes(binFile) : new byte[0]
                        });
                    }
                }

                string stateDir = StateDir(projectPath);
                if (Directory.Exists(stateDir))
                {
                    foreach (string jsonFile in Directory.EnumerateFiles(stateDir, "*.json"))
                    {
                        string name = Path.GetFileNameWithoutExtension(jsonFile);
                        snapshot.States.Add(new StateEntry
                        {
                            StatePath = projectPath + ProjectPaths.Separator + StateDirName + ProjectPaths.Separator + name,
                            Json = File.ReadAllText(jsonFile)
                        });
                    }
                }

                return true;
            }
        }
    }
}
