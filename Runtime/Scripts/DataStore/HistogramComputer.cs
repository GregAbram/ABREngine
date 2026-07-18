/* HistogramComputer.cs
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
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace IVLab.ABREngine.DataStore
{
    /// <summary>
    /// Computes a variable histogram from one KeyData object's binary, on
    /// the DataStore side -- so a consumer (e.g. abr_server) never needs
    /// direct filesystem access to compute this itself. Faithfully ports
    /// abr_server/api/views.py's histogram_binfile/get_histogram, including
    /// its existing binary-offset formula (which doesn't special-case
    /// Volume topology's lack of stored vertices) -- this is a bug-for-bug
    /// port, not a fix for anything beyond what moving the computation was
    /// asked to do. Timestep-based KeyData (separate .tstep files) isn't
    /// supported -- the DataStore's KeyData model doesn't represent those
    /// at all today, so this matches existing capability, not a new gap.
    ///
    /// Deliberately hand-rolls its own minimal field extraction rather than
    /// depending on a JSON library: this file must compile both in the
    /// plain .NET console app (DataStore.csproj) and under Unity/IL2CPP,
    /// and the KeyData json header has an always-flat, machine-generated
    /// shape, so a handful of targeted regexes are enough.
    /// </summary>
    public static class HistogramComputer
    {
        /// <summary>
        /// Attempt to compute a histogram for `variableLabel` from
        /// `keyDataJson`/`binData` (as returned by
        /// ILocalProjectStore.TryGetKeyData), split into `bins` equal-width
        /// bins spanning the KeyData's own recorded min/max for that
        /// variable. On success, `resultJson` is shaped exactly like
        /// abr_server's existing histogram response:
        /// {"histogram":[{"binMax":..,"items":..}, ...],"keyDataMin":..,
        /// "keyDataMax":..} -- the same "histogram"+2 boundary entries with
        /// zero items abr_server's UI already expects.
        /// </summary>
        public static bool TryCompute(string keyDataJson, byte[] binData, string variableLabel, int bins, out string resultJson, out string errorMessage)
        {
            string[] scalarArrayNames = ExtractStringArray(keyDataJson, "scalarArrayNames");
            int variableIndex = Array.IndexOf(scalarArrayNames, variableLabel);
            if (variableIndex < 0)
            {
                resultJson = null;
                errorMessage = $"No variable named {variableLabel}";
                return false;
            }

            float[] scalarMins = ExtractFloatArray(keyDataJson, "scalarMins");
            float[] scalarMaxes = ExtractFloatArray(keyDataJson, "scalarMaxes");
            int numPoints = ExtractInt(keyDataJson, "num_points");
            int numCellIndices = ExtractInt(keyDataJson, "num_cell_indices");

            double kdMin = scalarMins[variableIndex];
            double kdMax = scalarMaxes[variableIndex];

            // Same offset formula as histogram_binfile: vertices (3 floats/point)
            // + all scalar arrays before this one (numPoints floats each) +
            // the cell index array (numCellIndices ints, same 4 bytes).
            long offset = (long)numPoints * (3 + variableIndex) * 4 + (long)numCellIndices * 4;
            if (offset < 0 || offset + (long)numPoints * 4 > binData.Length)
            {
                resultJson = null;
                errorMessage = "KeyData binary is too short for the computed offset (timestep-based KeyData is not supported by gethistogram)";
                return false;
            }

            float[] data = new float[numPoints];
            Buffer.BlockCopy(binData, (int)offset, data, 0, numPoints * 4);

            // Uniform bin edges, same as np.linspace(kdMin, kdMax, bins).
            double[] edges = new double[bins];
            for (int i = 0; i < bins; i++)
            {
                edges[i] = kdMin + (kdMax - kdMin) * i / (bins - 1);
            }

            // np.histogram semantics for evenly-spaced edges: every bin is
            // half-open [edges[i], edges[i+1]) except the last, which is
            // closed on both ends; values outside [edges[0], edges[^1]]
            // aren't counted.
            int[] counts = new int[bins - 1];
            double span = kdMax - kdMin;
            foreach (float v in data)
            {
                if (v < kdMin || v > kdMax)
                {
                    continue;
                }
                int idx = span > 0 ? (int)((v - kdMin) / span * (bins - 1)) : 0;
                if (idx >= bins - 1)
                {
                    idx = bins - 2;
                }
                if (idx < 0)
                {
                    idx = 0;
                }
                counts[idx]++;
            }

            // Same shape as get_histogram's `zipped`: a synthetic
            // zero-item entry at keyDataMin, one entry per real bin (using
            // its right edge as binMax, matching bin_bound_list's [1:]
            // slice), then a synthetic zero-item entry at keyDataMax.
            var sb = new StringBuilder();
            sb.Append("{\"histogram\":[");
            AppendBin(sb, kdMin, 0);
            for (int i = 1; i < bins; i++)
            {
                sb.Append(',');
                AppendBin(sb, edges[i], counts[i - 1]);
            }
            sb.Append(',');
            AppendBin(sb, kdMax, 0);
            sb.Append("],\"keyDataMin\":");
            sb.Append(kdMin.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"keyDataMax\":");
            sb.Append(kdMax.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');

            resultJson = sb.ToString();
            errorMessage = null;
            return true;
        }

        private static void AppendBin(StringBuilder sb, double binMax, int items)
        {
            sb.Append("{\"binMax\":");
            sb.Append(binMax.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"items\":");
            sb.Append(items.ToString(CultureInfo.InvariantCulture));
            sb.Append('}');
        }

        private static int ExtractInt(string json, string key)
        {
            Match m = Regex.Match(json, $"\"{key}\"\\s*:\\s*(-?\\d+)");
            return int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        }

        private static string[] ExtractStringArray(string json, string key)
        {
            Match m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!m.Success || m.Groups[1].Value.Trim().Length == 0)
            {
                return Array.Empty<string>();
            }
            return Regex.Matches(m.Groups[1].Value, "\"(.*?)\"")
                .Select(mm => mm.Groups[1].Value)
                .ToArray();
        }

        private static float[] ExtractFloatArray(string json, string key)
        {
            Match m = Regex.Match(json, $"\"{key}\"\\s*:\\s*\\[(.*?)\\]", RegexOptions.Singleline);
            if (!m.Success || m.Groups[1].Value.Trim().Length == 0)
            {
                return Array.Empty<float>();
            }
            return m.Groups[1].Value.Split(',')
                .Select(s => float.Parse(s.Trim(), CultureInfo.InvariantCulture))
                .ToArray();
        }
    }
}
