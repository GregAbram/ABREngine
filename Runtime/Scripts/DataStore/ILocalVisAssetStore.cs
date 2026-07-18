/* ILocalVisAssetStore.cs
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
    /// One VisAsset's full bundle: its artifact.json header plus every
    /// other file it references (thumbnail, colormap.xml, geometry, etc),
    /// keyed by filename relative to the VisAsset's own folder. Exactly as
    /// received/stored -- never parsed beyond that top-level json/filename
    /// split.
    /// </summary>
    public class VisAssetEntry
    {
        /// <summary>The VisAsset's UUID (not a path -- VisAssets are global, not project-scoped).</summary>
        public string Uuid;
        public string Json;
        public Dictionary<string, byte[]> Files = new Dictionary<string, byte[]>();
    }

    /// <summary>
    /// A VisAsset's UUID + json header only (no file payloads) -- enough to
    /// populate a browse/palette UI without pulling potentially large
    /// geometry/texture data over the wire just to list what's available.
    /// </summary>
    public class VisAssetSummary
    {
        public string Uuid;
        public string Json;
    }

    /// <summary>
    /// Local persistence for VisAssets known to a DataStore node. Unlike
    /// ILocalProjectStore, this is global/UUID-keyed rather than
    /// project-scoped -- VisAssets aren't tied to any one Organization/
    /// Dataset -- and content is treated as immutable once a UUID exists
    /// (a re-save of the same UUID is a no-op in practice, but not
    /// specially guarded against here), so unlike projects/state there's no
    /// live subscription/fan-out for VisAssets, just get/list/push.
    /// </summary>
    public interface ILocalVisAssetStore
    {
        /// <summary>True if this VisAsset is already stored locally.</summary>
        bool HasVisAsset(string uuid);

        /// <summary>Persist (or overwrite) one VisAsset's json header + every file in its bundle.</summary>
        void SaveVisAsset(string uuid, string json, IReadOnlyDictionary<string, byte[]> files);

        /// <summary>Attempt to read back one VisAsset's full bundle.</summary>
        bool TryGetVisAsset(string uuid, out VisAssetEntry entry);

        /// <summary>UUID + json header (no file payloads) for every VisAsset currently known locally.</summary>
        IReadOnlyCollection<VisAssetSummary> ListVisAssetSummaries();
    }
}
