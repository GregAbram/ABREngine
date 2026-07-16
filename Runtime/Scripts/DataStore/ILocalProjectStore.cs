/* ILocalProjectStore.cs
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

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// One key data object's json header + binary payload, exactly as
    /// received/stored -- never parsed into RawDataset.
    /// </summary>
    public class KeyDataEntry
    {
        /// <summary>Full 4-segment key data path, e.g. "Org/Dataset/KeyData/Name".</summary>
        public string KeyDataPath;
        public string Json;
        public byte[] BinData;
    }

    /// <summary>
    /// The full known state of one project: its bounds (project.json), if
    /// any, plus every key data object stored for it.
    /// </summary>
    public class ProjectSnapshot
    {
        /// <summary>2-segment project path, e.g. "Org/Dataset".</summary>
        public string ProjectPath;

        /// <summary>Raw project.json text, or null if never received.</summary>
        public string ProjectInfoJson;

        public List<KeyDataEntry> KeyData = new List<KeyDataEntry>();
    }

    /// <summary>
    /// Local persistence for projects known to a DataStore node. Payloads
    /// are stored/returned as opaque strings/bytes -- this interface never
    /// deserializes into RawDataset/ProjectInfo (see design notes: doing so
    /// is lossy/asymmetric for vector data and time-varying datasets).
    /// </summary>
    public interface ILocalProjectStore
    {
        /// <summary>True if this project has any locally stored state (a project.json and/or at least one key data object).</summary>
        bool HasProject(string projectPath);

        /// <summary>Persist (or overwrite) a project's project.json contents.</summary>
        void SaveProjectInfo(string projectPath, string json);

        /// <summary>Persist (or overwrite) one key data object's json header + binary payload.</summary>
        void SaveKeyData(string keyDataPath, string json, byte[] binData);

        /// <summary>All 2-segment project paths currently known locally.</summary>
        IReadOnlyCollection<string> ListLocalProjects();

        /// <summary>Attempt to read back everything currently known about a project, for replaying to a new subscriber.</summary>
        bool TryReplayProject(string projectPath, out ProjectSnapshot snapshot);
    }
}
