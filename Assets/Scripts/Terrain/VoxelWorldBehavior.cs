using System;
using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;

public class VoxelWorldBehavior : MonoBehaviour, IConvertGameObjectToEntity
{
    // Chunks that are generated
    public GameObject ChunkPrefab;

    // How far can a player see
    public int RenderDistance = 5;

    // How far do we generate chunks
    public int MaxChunks = 3;

    // How big is a chunk
    public Vector3 ChunkSize = new Vector3(3,3,3);

    void IConvertGameObjectToEntity.Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {  
        dstManager.AddComponentData(entity, new VoxelWorld
        {          
            ChunkPrefab = GameObjectConversionUtility.ConvertGameObjectHierarchy(ChunkPrefab, World.Active),
            RenderDistance = RenderDistance,
            MaxChunks = MaxChunks,
            ChunkSize = new int3((int)ChunkSize.x, (int)ChunkSize.y, (int)ChunkSize.z),
            GameObjectId = GetInstanceID(),
            Entity = entity,
        });        
    }
}

public struct VoxelWorld : IComponentData
{
    public Entity Entity;
    public Entity ChunkPrefab;
    public int RenderDistance;
    public int MaxChunks;
    public int3 ChunkSize;
    public int GameObjectId;    
}

public class VoxelWorldSystem : ComponentSystem
{
    public NativeArray2D<Entity> ChunkMap;
    public bool IsInitialized;
    private ComponentGroup _worldGroup;
    public VoxelWorld VoxelWorld;
    private JobHandle _buildHandle;
    private bool _isWorldBuilt;
    private NativeArray<ComponentType> _worldComponents;

    protected override void OnCreateManager()
    {
        var components = new ComponentType[] { typeof(VoxelWorld) };
        _worldComponents = new NativeArray<ComponentType>(components, Allocator.Persistent);        
        _worldGroup = GetComponentGroup(new EntityArchetypeQuery { All = components });       
    }

    protected override void OnDestroyManager()
    {
        if (IsInitialized)
        {
            ChunkMap.Dispose();            
        }
        _worldComponents.Dispose();
    }

    protected override unsafe void OnUpdate()
    {
        if (!IsInitialized)
        {
            VoxelWorld = GetSingleton<VoxelWorld>();
            ChunkMap = new NativeArray2D<Entity>(VoxelWorld.ChunkSize.x, VoxelWorld.ChunkSize.z, Allocator.Persistent);

            if (!EntityManager.Exists(VoxelWorld.Entity))
            {
                throw new InvalidOperationException();
            }

            for (int x = 0; x < VoxelWorld.MaxChunks; x++)
            {
                for (int z = 0; z < VoxelWorld.MaxChunks; z++)
                {
                    var entity = EntityManager.Instantiate(VoxelWorld.ChunkPrefab);
                    var chunk = EntityManager.GetComponentData<VoxelChunk>(entity);
                    chunk.NeedsUpdate = 1;
                    chunk.Entity = entity;
                    chunk.VoxelWorldEntity = VoxelWorld.Entity;

                    chunk.X = x;
                    chunk.Z = z;
   
                    EntityManager.SetComponentData(entity, chunk);
                    ChunkMap[x, z] = entity;
                }
            }

            //Entities.ForEach((Entity e, ref VoxelChunk chunk) =>
            //{

            //});

            IsInitialized = true;
        }
    }

    public bool IsUpdateRequired { get; set; }
}

//struct BuildVoxelWorldJob : IJob
//{
//    public NativeArray2D<VoxelChunk> Chunks;
//    public VoxelWorld World;
//    public Vector3 WorldPosition;
//    public EntityCommandBuffer CommandBuffer;

//    public void Execute()
//    {
//        for (int x = 0; x < World.MaxChunks; x++)
//        {
//            for (int z = 0; z < World.MaxChunks; z++)
//            {
//                int3 offset = World.MaxChunks * World.ChunkSize / 2;
//                var xPosition = WorldPosition.x + x * World.ChunkSize.x - offset.x;
//                var zPosition = WorldPosition.z + z * World.ChunkSize.z - offset.z;
//                var position = new Vector3(xPosition, 0, zPosition);

//                Chunks[x, z] = GenerateChunk(x, z, position, World);
//            }
//        }
//    }

//    private VoxelChunk GenerateChunk(int x, int z, Vector3 position, VoxelWorld world)
//    {
//        var entity = CommandBuffer.Instantiate(world.ChunkPrefab);

//        CommandBuffer.SetComponent(entity, new Translation
//        {
//            Value = position
//        });

//        var chunk = new VoxelChunk
//        {
//            Entity = entity,
//            X = x,
//            Z = z,
//        };

//        CommandBuffer.SetComponent(entity, chunk);
//        return chunk;
//    }
//}

