/* DataStoreLog.cs
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

using System;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// This whole folder has no UnityEngine dependency (it compiles as a
    /// plain .NET console app too), so it can't call Debug.Log directly.
    /// Defaults to stderr for the standalone CLI; ABREngine.cs overrides
    /// this to route through Debug.LogWarning when running embedded in
    /// Unity, so failures that would otherwise be silently swallowed (e.g.
    /// a failed upstream connection) are actually visible.
    /// </summary>
    public static class DataStoreLog
    {
        public static Action<string> Warn = msg => Console.Error.WriteLine(msg);
    }
}
