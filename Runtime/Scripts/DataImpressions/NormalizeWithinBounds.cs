/* NormalizeWithinBounds.cs
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

using UnityEngine;

namespace IVLab.ABREngine
{
    /// <summary>
    /// Computes the transform that squishes a data-space bounding box into a
    /// container bounding box, uniformly (preserving the data's aspect ratio,
    /// centered within the container) rather than stretching it to fill the
    /// container on every axis.
    /// </summary>
    public static class NormalizeWithinBounds
    {
        public static void Normalize(Bounds container, Bounds dataBounds, out Matrix4x4 groupToDataMatrix, out Bounds groupBounds)
        {
            Vector3 dataSize = dataBounds.size;
            float maxDataDim = Mathf.Max(dataSize.x, Mathf.Max(dataSize.y, dataSize.z));
            if (maxDataDim <= Mathf.Epsilon)
            {
                maxDataDim = 1.0f;
            }

            float maxContainerDim = Mathf.Max(container.size.x, Mathf.Max(container.size.y, container.size.z));
            float scale = maxContainerDim / maxDataDim;

            groupToDataMatrix =
                Matrix4x4.Translate(container.center) *
                Matrix4x4.Scale(Vector3.one * scale) *
                Matrix4x4.Translate(-dataBounds.center);

            groupBounds = new Bounds(container.center, dataSize * scale);
        }
    }
}
