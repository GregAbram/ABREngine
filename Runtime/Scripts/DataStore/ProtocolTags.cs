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

        /// <summary>
        /// Request the name + json header (no binary payload) of every key
        /// data object known locally for one project -- a cheap browse
        /// query for populating a UI palette, deliberately separate from
        /// "subscribe" for the same reason as "liststates".
        /// </summary>
        public const string ListKeyData = "listkeydata";

        /// <summary>
        /// Designates one named state as "the active one" for this node --
        /// a single, node-wide pointer (not per-project) that a FE sets
        /// when a user opens a project, so downstream consumers can follow
        /// "whatever's currently being edited" without knowing which
        /// project that is ahead of time. Requires a live AttachFE lease
        /// from the same session; rejected if a different session currently
        /// holds the lease.
        /// </summary>
        public const string SetActive = "setactive";

        /// <summary>
        /// Subscribe to whatever project+state is currently active on this
        /// node (no project path given -- that's the point) -- chains
        /// upstream if this node has no active designation of its own, the
        /// same way EnsureAvailableAsync does for a named project. Reply
        /// carries the resolved active state path before the usual replay
        /// begins, and the connection stays open afterward: if a later
        /// SetActive points somewhere new (even a different project), this
        /// subscriber transparently gets a fresh replay of it.
        /// </summary>
        public const string SubscribeActive = "subscribeactive";

        /// <summary>
        /// Periodic heartbeat/claim from a FE, carrying an opaque session
        /// id generated once at that FE's startup. Enforces "only one FE
        /// attached at a time": accepted if no session currently holds the
        /// lease, if this is the same session renewing it, or if the
        /// current lease has gone stale (no heartbeat within the timeout --
        /// see SubscriptionHub.FeLeaseTimeout); rejected otherwise. Django's
        /// request/response model can't hold a single connection open for a
        /// process's whole lifetime, so this is a lease renewed by repeated
        /// one-shot calls, not one persistent connection.
        /// </summary>
        public const string AttachFE = "attachfe";

        /// <summary>
        /// Request one named state's json in a single one-shot round trip
        /// (label = full 4-segment state path) -- a cheap fetch for a
        /// consumer that just needs that one state's content, deliberately
        /// separate from "subscribe" for the same reason as "liststates"/
        /// "listkeydata". Chains upstream first via EnsureAvailableAsync,
        /// the same as those two. Reply is the ack frame; if "ok", one
        /// length-prefixed json string follows.
        /// </summary>
        public const string GetState = "getstate";

        /// <summary>Literal ack string for a successful request.</summary>
        public const string Ok = "ok";

        /// <summary>Prefix for a failed request's ack string: "error:&lt;message&gt;".</summary>
        public const string ErrorPrefix = "error:";
    }
}
