using Unity.Collections;
using UnityEngine;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using Unity.Transforms;
using Random = UnityEngine.Random;

public class VoxelChunkBehavior : MonoBehaviour, IConvertGameObjectToEntity
{
    public GameObject VoxelGrassPrefab;
    public GameObject VoxelEarthPrefab;

    void IConvertGameObjectToEntity.Convert(Entity entity, EntityManager dstManager, GameObjectConversionSystem conversionSystem)
    {
        dstManager.AddComponentData(entity, new VoxelChunk
        {
            VoxelPrefab1 = GameObjectConversionUtility.ConvertGameObjectHierarchy(VoxelGrassPrefab, World.Active),
            VoxelPrefab2 = GameObjectConversionUtility.ConvertGameObjectHierarchy(VoxelEarthPrefab, World.Active),
        });
    }
}

public struct VoxelChunk : IComponentData
{
    public Entity Entity;
    public Entity VoxelPrefab1;
    public Entity VoxelPrefab2;
    public Entity VoxelWorldEntity;
    public int Z;
    public int X;
    public int NeedsUpdate;
    public int IsPlayerChunk;
    public int IsInitialized;
    public int IsPopulated;
}

public class VoxelChunkSystem : JobComponentSystem
{
    private const float _noiseAmplification = 5f;
    private const float _noiseFrequency = 10f;
    private const float _noiseSeed = 99;

    public NativeArray3D<Entity> VoxelsMap;

    private VoxelWorldSystem _voxelWorldSystem;
    private JobHandle _handle;
    private EntityCommandBuffer _commands;
    private ComponentGroup _chunkGroup;
    private bool _isInitialized;


    protected override void OnCreateManager()
    {
        _commands = new EntityCommandBuffer(Allocator.Persistent);
        _voxelWorldSystem = World.GetOrCreateManager<VoxelWorldSystem>();
        _chunkGroup = GetComponentGroup(new EntityArchetypeQuery { All = new ComponentType[]
        {
            typeof(VoxelChunk),
            typeof(Translation)
        }});
    }

    protected override void OnDestroyManager()
    {
        VoxelsMap.Dispose();
        _commands.Dispose();
    }

    private struct UpdateChunkPosition : IJobProcessComponentDataWithEntity<Translation, VoxelChunk>
    {
        public VoxelWorld World;
        public Vector3 WorldPosition;

        public void Execute(Entity entity, int index, ref Translation translation, ref VoxelChunk chunk)
        {
            if (chunk.NeedsUpdate == 1)
            {
                int3 offset = World.MaxChunks * World.ChunkSize / 2;
                float x = WorldPosition.x + chunk.X * World.ChunkSize.x - offset.x;
                float z = WorldPosition.z + chunk.Z * World.ChunkSize.z - offset.z;
                float y = WorldPosition.y;
                translation.Value = new Vector3(x, y, z);
                chunk.NeedsUpdate = 0;
                chunk.IsInitialized = 1;
            }
        }
    }

    protected override JobHandle OnUpdate(JobHandle inputDeps)
    {
        if (!_voxelWorldSystem.IsInitialized)
            return inputDeps;

        if (_isInitialized)
            return inputDeps;

        var xzSize = _voxelWorldSystem.VoxelWorld.ChunkSize * _voxelWorldSystem.VoxelWorld.MaxChunks;

        VoxelsMap = new NativeArray3D<Entity>(xzSize.x, _voxelWorldSystem.VoxelWorld.ChunkSize.y, xzSize.z, Allocator.Persistent);

        _isInitialized = true;
        
        var worldPosition = GetComponentDataFromEntity<Translation>(true)[_voxelWorldSystem.VoxelWorld.Entity].Value;
        var World = _voxelWorldSystem.VoxelWorld;
        var et = _chunkGroup.ToEntityArray(Allocator.Persistent);
        var tmp = _chunkGroup.ToComponentDataArray<VoxelChunk>(Allocator.Persistent);
        var pos = _chunkGroup.ToComponentDataArray<Translation>(Allocator.Persistent);

        for (int i = 0; i < tmp.Length; i++)
        {
            var chunkEntity = et[i];
            var voxelChunk = tmp[i];
            var translation = pos[i];

            if (voxelChunk.NeedsUpdate == 1)
            {
                int3 offset = World.MaxChunks * World.ChunkSize / 2;
                float x = worldPosition.x + voxelChunk.X * World.ChunkSize.x - offset.x;
                float z = worldPosition.z + voxelChunk.Z * World.ChunkSize.z - offset.z;
                float y = worldPosition.y;
                translation.Value = new Vector3(x, y, z);

                EntityManager.SetComponentData(chunkEntity, translation);

                voxelChunk.NeedsUpdate = 0;
                voxelChunk.IsInitialized = 1;

                EntityManager.SetComponentData(chunkEntity, voxelChunk);
            }

            if (voxelChunk.IsInitialized == 1 && voxelChunk.IsPopulated == 0)
            {
                var terrainHeight = GenerateTerrainHeight(voxelChunk, translation, _voxelWorldSystem.VoxelWorld.ChunkSize);

                for (int x = 0; x < World.ChunkSize.x; x++)
                for (int y = 0; y < World.ChunkSize.y; y++)                          
                for (int z = 0; z < World.ChunkSize.z; z++)
                {
                    Vector3 position = new float3(translation.Value.x + x, translation.Value.y + y, translation.Value.z + z);
                    Entity voxelEntity;

                    switch (terrainHeight[x, y, z])
                    {
                        case 1:
                            voxelEntity = EntityManager.Instantiate(voxelChunk.VoxelPrefab1);
                            break;
                        case 2:
                            voxelEntity = EntityManager.Instantiate(voxelChunk.VoxelPrefab2);
                            break;
                        default:
                            continue;
                    }

                    EntityManager.SetComponentData(voxelEntity, new Voxel
                    {
                        Entity = voxelEntity,
                        VoxelChunkEntity = chunkEntity,
                        X = x,
                        Y = y,
                        Z = z,
                        Initialized = 1
                    });

                    EntityManager.SetComponentData(voxelEntity, new Translation
                    {
                        Value = position
                    });

                    VoxelsMap[x, y, z] = voxelEntity;
                }
               
                voxelChunk.IsPopulated = 1;
            }
        }

        et.Dispose();
        tmp.Dispose();
        pos.Dispose();
        return inputDeps;
    }

    private int[,,] GenerateTerrainHeight(VoxelChunk c, Translation t, int3 chunkSize)
    {
        int[,,] voxelTypes = new int[chunkSize.x, 256, chunkSize.z];

        const int height = 5;

        var offset = Mathf.Max(1, Mathf.Min(chunkSize.y, chunkSize.y - height));

        for (int x = 0; x < chunkSize.x; x++)
        {
            for (int z = 0; z < chunkSize.z; z++)
            {
                Vector3 position = t.Value;

                // Add current voxel position, the subtraction accounts for the overhead we generate
                position.x += x;
                position.z += z;

                int baseY = Mathf.FloorToInt(Mathf.PerlinNoise((1000000f + position.x) / _noiseFrequency, (_noiseSeed + 1000000f + position.z) / _noiseFrequency) * _noiseAmplification + offset);

                var level1 = math.max(height, offset);

                for (int y = 0; y < baseY; y++)
                {
                    if (y < level1 && y < baseY-1)
                    {
                        voxelTypes[x, y, z] = 2;
                    }
                    else
                    {
                        voxelTypes[x, y, z] = 1;
                    }
                }
            }
        }

        return voxelTypes;
    }
}


//public unsafe struct TestJob : IJobChunk
//{
//    public VoxelWorldEntity World;
//    public NativeArray2D<VoxelChunk> ChunksMap;
//    public ArchetypeChunkComponentType<Translation> Translations;
//    public ArchetypeChunkComponentType<VoxelChunk> VoxelChunks;

//    public void Execute(ArchetypeChunk chunk, int chunkIndex, int firstEntityIndex)
//    {
//        if (chunk.Has(VoxelChunks))
//        {
//            using (var translationsArr = chunk.GetNativeArray(Translations))
//            using (var voxelChunksArr = chunk.GetNativeArray(VoxelChunks))
//            {
//                void* translations = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(translationsArr);
//                void* voxelChunks = NativeArrayUnsafeUtility.GetUnsafeBufferPointerWithoutChecks(voxelChunksArr);

//                for (int i = 0; i < voxelChunksArr.Length; i++)
//                {
//                    ref var voxelChunk = ref UnsafeUtilityEx.ArrayElementAsRef<VoxelChunk>(voxelChunks, i);
//                    ref var translation = ref UnsafeUtilityEx.ArrayElementAsRef<Translation>(translations, i);

//                    translation.Value.x = 5;
//                    translation.Value.z = 8;
//                }
//            }
//        }
//    }
//}


//private struct PopulateChunk : IJobProcessComponentDataWithEntity<Translation, VoxelChunk>
//{
//    public VoxelWorld World;
//    public EntityCommandBuffer Commands;
//    public NativeArray3D<Entity> VoxelsMap;        

//    public void Execute(Entity entity, int index, ref Translation t, ref VoxelChunk chunk)
//    {
//        if (chunk.IsInitialized == 1 && chunk.IsPopulated == 0)
//        {
//            for (int x = 0; x < World.ChunkSize.x; x++)
//            for (int y = 0; y < World.ChunkSize.y; y++)
//            for (int z = 0; z < World.ChunkSize.z; z++)
//            {
//                Vector3 position = new float3(t.Value.x + x, t.Value.y + y, t.Value.z + z);

//                var voxelEntity = Commands.Instantiate(chunk.VoxelPrefab);        

//                Commands.SetComponent(entity, new Voxel
//                {
//                    Entity = voxelEntity,
//                    VoxelChunkEntity = entity,
//                    X = x,
//                    Y = y,
//                    Z = z,
//                    Initialized = 1
//                });

//                Commands.SetComponent(entity, new Translation
//                {
//                    Value = position
//                });

//                VoxelsMap[x, y, z] = voxelEntity;
//            }
//            chunk.IsPopulated = 1;
//        }          
//    }
//}