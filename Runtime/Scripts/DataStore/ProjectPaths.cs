/* ProjectPaths.cs
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
    /// Minimal, self-contained reimplementation of the path convention
    /// documented in Runtime/Scripts/Data/DataPath.cs: a full key data path
    /// is "Organization/Dataset/KeyData/Name" (4 segments); a project path
    /// is the first 2 segments, "Organization/Dataset". Reimplemented here
    /// (rather than referencing DataPath.cs directly) so this folder has no
    /// dependency on the rest of Runtime/Scripts/Data, which is required for
    /// it to also compile as a plain .NET console app.
    /// </summary>
    public static class ProjectPaths
    {
        public const char Separator = '/';

        /// <summary>Extract the 2-segment project path from a longer (e.g. 4-segment key data) path.</summary>
        public static string GetProjectPath(string path)
        {
            string[] parts = path.Split(Separator);
            if (parts.Length < 2)
            {
                throw new ArgumentException($"Path \"{path}\" does not contain at least 2 segments (Organization/Dataset)");
            }
            return parts[0] + Separator + parts[1];
        }

        /// <summary>True if `path` is exactly a 2-segment "Organization/Dataset" project path.</summary>
        public static bool IsProjectPath(string path)
        {
            return path.Split(Separator).Length == 2;
        }

        /// <summary>The last path segment (e.g. the key data object's name).</summary>
        public static string GetName(string path)
        {
            string[] parts = path.Split(Separator);
            return parts[parts.Length - 1];
        }
    }
}
