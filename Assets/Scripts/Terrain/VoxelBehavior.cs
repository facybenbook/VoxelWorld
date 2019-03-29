using System.Collections;
using System.Collections.Generic;
using Unity.Entities;
using UnityEngine;

public struct Voxel: IComponentData
{
    public Entity Entity;
    public Entity VoxelWorldEntity;
    public Entity VoxelChunkEntity;
    public int Y;
    public int X;
    public int Z;
    public int Initialized;
}

public class VoxelBehavior : MonoBehaviour, IConvertGameObjectToEntity
{
    public void Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new Voxel
        {
            Entity = entity,            
        });
    }
}

