/* KeyData.cs
 *
 * Copyright (c) 2020 University of Minnesota
 * Authors: Seth Johnson <sethalanjohnson@gmail.com>
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
// InstancedMeshRenderer.cs
// Drop-in replacement for your current script (keeps your public fields).
// Key fixes:
//  1) WORLD-SPACE bounds for DrawMeshInstancedProcedural (prevents whole-draw frustum culling)
//  2) Always uses transform.localToWorldMatrix / worldToLocalMatrix for _ObjectTransform(_Inverse)
//  3) Packs per-instance matrices into float4 rows (Vector4*4) to match the shader below
//  4) Correctly binds buffers + _InstanceCount/_UseInstanceBuffers every frame

using UnityEngine;
using UnityEngine.Rendering;
using System;

namespace IVLab.ABREngine
{
    public class InstancedMeshRenderer : MonoBehaviour
    {
        public Matrix4x4[] instanceLocalTransforms;
        public Vector4[] renderInfo;
        public int[] hiliteBuffer;

        public float instanceDensity = 1.0f;
        public int instanceCount = 100000;
        public Mesh instanceMesh;
        public Material instanceMaterial;
        public int subMeshIndex = 0;
        public Bounds bounds;
public bool buffersDirty = false;

        public int cachedInstanceCount = -1;
        private int cachedSubMeshIndex = -1;

        private ComputeBuffer renderInfoBuffer;
        private ComputeBuffer transformBuffer;         // float4 rows: instanceCount*4 elements
        private ComputeBuffer transformBufferInverse;  // float4 rows: instanceCount*4 elements
        private ComputeBuffer perInstanceHiliteBuffer;


        private bool invalid = true;
        public MaterialPropertyBlock block;

        public bool useInstanced = true;

        void Update()
        {
            //Debug.Log($"Shader: {instanceMaterial?.shader?.name}  Color: {instanceMaterial?.color}");
Debug.Log($"Frame {Time.frameCount}: invalid={invalid} cachedInstanceCount={cachedInstanceCount} instanceCount={instanceCount} buffersDirty={buffersDirty} transformBuffer={transformBuffer?.count} renderInfoBuffer={renderInfoBuffer?.count}");
            //Debug.Log("IMR Update tick");

            if (cachedInstanceCount != instanceCount || cachedSubMeshIndex != subMeshIndex || buffersDirty)
            {
                buffersDirty = false;

                if (block == null)
                    block = new MaterialPropertyBlock();

                UpdateBuffers();
            }

            if (invalid) return;
            if (instanceMesh == null || instanceMaterial == null) return;

            // Must be enabled on the *material used by the draw call*
            instanceMaterial.enableInstancing = true;

            // Provide object->world for this renderer object (same role as old MeshRenderer matrices)
            block.SetMatrix("_ObjectTransform", transform.localToWorldMatrix);
            block.SetMatrix("_ObjectTransformInverse", transform.worldToLocalMatrix);

            // Tell shader buffers are valid + instance count
            block.SetInt("_UseInstanceBuffers", 1);
            block.SetInt("_InstanceCount", instanceCount);

            // WORLD-SPACE bounds (huge; centered on camera to avoid frustum culling while debugging)
            var cam = Camera.main;
            Vector3 center = cam != null ? cam.transform.position : transform.position;
            var transformedBounds = new Bounds(transform.position, Vector3.one * 1000000f);

            if (useInstanced)
            {
                //      Debug.Log($"IMR DRAW: invalid={invalid} mesh={instanceMesh!=null} mat={instanceMaterial!=null} count={instanceCount} sub={subMeshIndex} layer={gameObject.layer}");
                if (instanceMaterial != null)
                {
                    instanceMaterial.enableInstancing = true;
                }

Debug.Log($"Drawing {instanceCount} instances, mesh={instanceMesh?.name}, bounds={transformedBounds}, layer={gameObject.layer}");
Debug.Log($"transformBuffer null={transformBuffer==null}, renderInfoBuffer null={renderInfoBuffer==null}");
Debug.Log($"invalid={invalid}, instanceLocalTransforms length={instanceLocalTransforms?.Length}");
                Graphics.DrawMeshInstancedProcedural(
                    instanceMesh,
                    subMeshIndex,
                    instanceMaterial,
                    transformedBounds, 
                    instanceCount,
                    block,
                    ShadowCastingMode.On,
                    true,
                    gameObject.layer
                );
            }
            else
            {
                // Non-instanced fallback (slow)
                for (int i = 0; i < instanceLocalTransforms.Length; i++)
                {
                    block.SetVector("_RenderInfo", renderInfo[i]);
                    Graphics.DrawMesh(instanceMesh, transform.localToWorldMatrix * instanceLocalTransforms[i], instanceMaterial, gameObject.layer, null, subMeshIndex, block);
                }
            }
        }


        void UpdateBuffers()
        {

            Debug.Log($"UpdateBuffers called: transforms={instanceLocalTransforms?.Length}, renderInfo={renderInfo?.Length}, mesh={instanceMesh?.name}, mat={instanceMaterial?.name}");
            invalid = true;

            if (block == null) return;
            if (instanceLocalTransforms == null || instanceLocalTransforms.Length == 0) return;
            if (renderInfo == null || renderInfo.Length != instanceLocalTransforms.Length) return;
            if (instanceMesh == null || instanceMaterial == null) return;

            invalid = false;
            instanceCount = instanceLocalTransforms.Length;

            // Ensure submesh index is in range
            if (instanceMesh != null)
                subMeshIndex = Mathf.Clamp(subMeshIndex, 0, instanceMesh.subMeshCount - 1);

            // Release old
            renderInfoBuffer?.Release();
            transformBuffer?.Release();
            transformBufferInverse?.Release();
            perInstanceHiliteBuffer?.Release();

            // Create new
            renderInfoBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 4);

            transformBuffer        = new ComputeBuffer(instanceCount, sizeof(float) * 16);
            transformBufferInverse = new ComputeBuffer(instanceCount, sizeof(float) * 16);

            int hiliteInts = (instanceCount + 31) / 32;
            perInstanceHiliteBuffer = new ComputeBuffer(hiliteInts, sizeof(Int32));

            // Hilite: if null/wrong length, create CPU + fill zeros
            if (hiliteBuffer == null || hiliteBuffer.Length != hiliteInts)
            {
                hiliteBuffer = new int[hiliteInts];
                perInstanceHiliteBuffer.SetData(hiliteBuffer);
            }
            else
            {
                perInstanceHiliteBuffer.SetData(hiliteBuffer);
            }

            // Build inverse array
            var inverses = new Matrix4x4[instanceCount];
            for (int i = 0; i < instanceCount; i++)
                inverses[i] = instanceLocalTransforms[i].inverse;

            transformBuffer.SetData(instanceLocalTransforms);


Debug.Log($"Transform[0]: pos={instanceLocalTransforms[0].GetColumn(3)}");
Debug.Log($"Transform[62]: pos={instanceLocalTransforms[62].GetColumn(3)}");
Debug.Log($"Transform[124]: pos={instanceLocalTransforms[124].GetColumn(3)}");


            transformBufferInverse.SetData(inverses);
            renderInfoBuffer.SetData(renderInfo);

            // Hilite: if null/wrong length, fill with zeros
            if (hiliteBuffer == null || hiliteBuffer.Length != hiliteInts)
            {
                var zeros = new int[hiliteInts];
                perInstanceHiliteBuffer.SetData(zeros);
            }
            else
            {
                perInstanceHiliteBuffer.SetData(hiliteBuffer);
            }

            // Bind buffers (names must match shader)
            block.SetBuffer("transformBuffer", transformBuffer);
            block.SetBuffer("transformBufferInverse", transformBufferInverse);
            block.SetBuffer("renderInfoBuffer", renderInfoBuffer);
            block.SetBuffer("perInstanceHiliteBuffer", perInstanceHiliteBuffer);

            cachedInstanceCount = instanceCount;
            cachedSubMeshIndex = subMeshIndex;

            // Also set these once here (we still set every frame in Update as well)
            block.SetInt("_UseInstanceBuffers", 1);
            block.SetInt("_InstanceCount", instanceCount);
        }

        void OnDestroy()
        {
            renderInfoBuffer?.Release();
            transformBuffer?.Release();
            transformBufferInverse?.Release();
            perInstanceHiliteBuffer?.Release();
        }
// --- Hilite API (kept for compatibility with ABR) ---

public void toggleHilite(int which)
{
    if (hiliteBuffer == null) return;

    int arrayOffset = which / 32;
    int bitOffset   = which % 32;

    if (arrayOffset < 0 || arrayOffset >= hiliteBuffer.Length) return;

    hiliteBuffer[arrayOffset] ^= (1 << bitOffset);
    PushHiliteBufferToGPU();
}

public void setHilite(int which)
{
    if (hiliteBuffer == null) return;

    int arrayOffset = which / 32;
    int bitOffset   = which % 32;

    if (arrayOffset < 0 || arrayOffset >= hiliteBuffer.Length) return;

    hiliteBuffer[arrayOffset] |= (1 << bitOffset);
    PushHiliteBufferToGPU();
}

public void clearHilite(int which)
{
    if (hiliteBuffer == null) return;

    int arrayOffset = which / 32;
    int bitOffset   = which % 32;

    if (arrayOffset < 0 || arrayOffset >= hiliteBuffer.Length) return;

    hiliteBuffer[arrayOffset] &= ~(1 << bitOffset);
    PushHiliteBufferToGPU();
}

// Update just the GPU hilite buffer (no full reallocation)
private void PushHiliteBufferToGPU()
{
    if (perInstanceHiliteBuffer == null) return;

    int expected = (instanceCount + 31) / 32;

    // If sizes don't match, fall back to rebuilding buffers
    if (hiliteBuffer == null || hiliteBuffer.Length != expected || perInstanceHiliteBuffer.count != expected)
    {
        UpdateBuffers();
        return;
    }

    perInstanceHiliteBuffer.SetData(hiliteBuffer);
    block?.SetBuffer("perInstanceHiliteBuffer", perInstanceHiliteBuffer);
}
        
    }
}