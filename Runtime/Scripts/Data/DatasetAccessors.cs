
using System.Linq;
using IVLab.ABREngine;
using UnityEngine;


public class DatasetAccessor
{
    protected RawDataset dataset;
    protected IDataImpression impression;

    public DatasetAccessor(RawDataset dset, IDataImpression idi)
    {
        dataset = dset;
        impression = idi;
    }

    public  string GetPath()
    {
        return dataset.dataPath;
    }

    public string[] GetScalarVariableNames()
    {
        return dataset?.scalarArrayNames;
    }    
    
    public string[] GetVectorVariableNames()
    {
        return dataset?.vectorArrayNames;
    }


    public string GetColorVariableName()
    {        
        string colorArrayName = impression.GetColorVariable().Path.Split('/').Last();
        return "foo";
    }
}

public class SurfaceDatasetAccessor : DatasetAccessor
{            
    public SurfaceDatasetAccessor(RawDataset dset, IDataImpression idi) : base(dset, idi) {}

    public bool GetScalarValue(ABRPicker.ABRPick pick, out float value)
    {
        SimpleSurfaceDataImpression surfaceImpression = impression as SimpleSurfaceDataImpression;
        SimpleSurfaceRenderInfo renderInfo = surfaceImpression.RenderInfo as SimpleSurfaceRenderInfo;
        Debug.Log(renderInfo);
        Debug.Log(impression);

        string colorArrayName = surfaceImpression.colorVariable.Path.Split('/').Last();

        int indx = -1;
        string[] scalarArrays = GetScalarVariableNames();
        for (int i = 0; i  < dataset.scalarArrayNames.Length && indx == -1; i++)
            if (colorArrayName == dataset.scalarArrayNames[i]) 
                indx = i;

        if (indx == -1)
        {
            value = 0;
            return false;
        }

        SerializableFloatArray data = dataset.scalarArrays[indx];

        // pick.id is in the mesh - which doubles the number of triangles to handle backfacers.
        float p, q, r;
        int ip, iq, ir;

        int id = pick.id;
        if (pick.id >= dataset.cellIndexOffsets.Length)
        {
            id = id - dataset.cellIndexOffsets.Length;
            int triangleIndex = dataset.cellIndexOffsets[id];             
            ip = dataset.indexArray[triangleIndex + 2];
            iq = dataset.indexArray[triangleIndex + 1];
            ir = dataset.indexArray[triangleIndex + 0];         
        }
        else
        {
            int triangleIndex = dataset.cellIndexOffsets[id];       
            ip = dataset.indexArray[triangleIndex + 0];
            iq = dataset.indexArray[triangleIndex + 1];
            ir = dataset.indexArray[triangleIndex + 2];         
        }

        p = data.array[ip];
        q = data.array[iq];
        r = data.array[ir];

        value = pick.barycentric_weights[0] * p + pick.barycentric_weights[1] * q + pick.barycentric_weights[2] * r;
        Debug.Log(p + " " + q + " " + r + " = " + value);

        return true;
    }
}