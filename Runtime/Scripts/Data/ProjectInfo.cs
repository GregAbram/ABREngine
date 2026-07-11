/* ProjectInfo.cs
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
using System.IO;
using UnityEngine;

namespace IVLab.ABREngine
{
    /// <summary>
    /// Project-level metadata that lives alongside a dataset's key data
    /// .json/.bin files (one project.json per dataset directory). Carries
    /// the true data-space bounds of the original, top-level dataset -- as
    /// opposed to the bounds of any one key data object (e.g. a contour or
    /// slice) derived from it, which may cover only part of the original
    /// extent.
    /// </summary>
    [Serializable]
    public class ProjectInfo
    {
        public Bounds bounds;

        public const string FileName = "project.json";

        /// <summary>
        /// Attempt to load project.json from a dataset's directory (the same
        /// directory its key data .json/.bin files are cached in).
        /// </summary>
        public static bool TryLoad(string datasetDir, out Bounds bounds)
        {
            bounds = new Bounds();
            string path = Path.Combine(datasetDir, FileName);
            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                ProjectInfo info = JsonUtility.FromJson<ProjectInfo>(json);
                bounds = info.bounds;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError("Unable to load " + path + ": " + e);
                return false;
            }
        }

        /// <summary>
        /// Save (or overwrite) project.json in a dataset's directory.
        /// </summary>
        public static void Save(string datasetDir, Bounds bounds)
        {
            Directory.CreateDirectory(datasetDir);
            string path = Path.Combine(datasetDir, FileName);
            ProjectInfo info = new ProjectInfo { bounds = bounds };
            File.WriteAllText(path, JsonUtility.ToJson(info, true));
        }
    }
}
