/* ProtocolTags.cs
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

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Message tags used on the DataStore wire protocol. The first three
    /// match the tags already sent by Tools/SendToABR.py and understood by
    /// SocketDataListener; "subscribe" and "listprojects" are new.
    /// </summary>
    public static class ProtocolTags
    {
        /// <summary>Bare control message: flush/render barrier.</summary>
        public const string Update = "update";

        /// <summary>Whole-dataset bounds (project.json equivalent).</summary>
        public const string ProjectInfo = "projectinfo";

        /// <summary>
        /// One key data object's json header + binary payload. Note: unlike
        /// Update/ProjectInfo, any tag that isn't recognized elsewhere is
        /// historically treated as "keydata" by SocketDataListener -- this
        /// constant is the canonical value new code should send.
        /// </summary>
        public const string KeyData = "keydata";

        /// <summary>
        /// Request a project by path; if accepted, the connection stays
        /// open for a full-state replay followed by a live tail.
        /// </summary>
        public const string Subscribe = "subscribe";

        /// <summary>
        /// Request the set of known project paths, recursively merged with
        /// any configured upstream (upstream entries win name collisions).
        /// </summary>
        public const string ListProjects = "listprojects";

        /// <summary>
        /// One named visualization state's json (styling config -- which
        /// KeyData gets which glyph/color/VisAsset). A project can have
        /// multiple named states.
        /// </summary>
        public const string State = "state";

        /// <summary>
        /// Request the set of state names known locally for one project
        /// (a cheap, one-shot browse query -- deliberately separate from
        /// "subscribe", which opens a long-lived connection and replays all
        /// of a project's key data binaries too).
        /// </summary>
        public const string ListStates = "liststates";

        /// <summary>Literal ack string for a successful request.</summary>
        public const string Ok = "ok";

        /// <summary>Prefix for a failed request's ack string: "error:&lt;message&gt;".</summary>
        public const string ErrorPrefix = "error:";
    }
}
