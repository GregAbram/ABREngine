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
    /// One named visualization state (styling config -- which KeyData gets
    /// which glyph/color/VisAsset) belonging to a project, exactly as
    /// received/stored -- pure JSON, never parsed. A project can have
    /// multiple named states (e.g. alternate styling configs for the same
    /// data).
    /// </summary>
    public class StateEntry
    {
        /// <summary>Full 4-segment state path, e.g. "Org/Dataset/State/Name".</summary>
        public string StatePath;
        public string Json;
    }

    /// <summary>
    /// The full known state of one project: its bounds (project.json), if
    /// any, plus every key data object and named visualization state
    /// stored for it.
    /// </summary>
    public class ProjectSnapshot
    {
        /// <summary>2-segment project path, e.g. "Org/Dataset".</summary>
        public string ProjectPath;

        /// <summary>Raw project.json text, or null if never received.</summary>
        public string ProjectInfoJson;

        public List<KeyDataEntry> KeyData = new List<KeyDataEntry>();

        public List<StateEntry> States = new List<StateEntry>();
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

        /// <summary>Persist (or overwrite) one named visualization state's json.</summary>
        void SaveState(string statePath, string json);

        /// <summary>All 2-segment project paths currently known locally.</summary>
        IReadOnlyCollection<string> ListLocalProjects();

        /// <summary>All state names (not full paths) currently known locally for a project.</summary>
        IReadOnlyCollection<string> ListStateNames(string projectPath);

        /// <summary>Attempt to read back everything currently known about a project, for replaying to a new subscriber.</summary>
        bool TryReplayProject(string projectPath, out ProjectSnapshot snapshot);
    }
}
