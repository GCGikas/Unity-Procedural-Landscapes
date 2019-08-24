using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MeshGen : MonoBehaviour
{
    Mesh mesh;
    Vector3[] vertices;
    int[] triangles;
    System.Random rand = new System.Random();
    float[,] heightmap;
    int xSize = 200;
    int zSize = 200;

    //Droplet erosion values
    int maxSteps = 100;
    float inertia = 0.1f;
    float pMinSlope = 0.05f;
    float pCapacity = 8f;
    float pDeposition = 0.02f;
    float pErosion = 0.9f;
    float pEvaporation = 0.0125f;
    float pGravity = 10f;
    float pRadius = 4f;

    // Start is called before the first frame update
    void Start()
    {
        mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;

        BuildMesh();
        UpdateMesh();
    }

    void BuildMesh()
    {
        vertices = new Vector3[(xSize + 1) * (zSize + 1)];
        heightmap = new float[xSize + 1, zSize + 1];

        int i = 0;
        for (int z = 0; z <= zSize; z++)
        {
            for (int x = 0; x <= xSize; x++)
            {
                float y = Mathf.PerlinNoise(x * .015f, z * .015f) * 40f;
                vertices[i] = new Vector3(x, y, z);
                heightmap[x, z] = y;
                i++;
            }
        }

        Debug.Log(i + " times");

        triangles = new int[xSize * zSize * 6];
        int vert = 0;
        int tris = 0;

        for (int z = 0; z < zSize; z++)
        {
            for (int x = 0; x < xSize; x++)
            {
                triangles[tris] = vert;
                triangles[tris + 1] = vert + xSize + 1;
                triangles[tris + 2] = vert + 1;
                triangles[tris + 3] = vert + 1;
                triangles[tris + 4] = vert + xSize + 1;
                triangles[tris + 5] = vert + xSize + 2;

                vert++;
                tris += 6;
            }
            vert++;
        }

        SimulateErosion(100000);
    }

    void UpdateMesh()
    {
        mesh.Clear();
        mesh.vertices = vertices;
        mesh.triangles = triangles;

        mesh.RecalculateNormals();
    }

    void SimulateDropletStep(Droplet droplet)
    {
        if (droplet.stepsTaken > maxSteps)
        {
            return;
        }

        int LLNodeX = (int) droplet.x;
        int LLNodeZ = (int) droplet.z;

        float xPosOff = droplet.x - LLNodeX;
        float zPosOff = droplet.z - LLNodeZ;

        if (droplet.x > xSize || droplet.z > zSize)
        {
            Debug.Log("Why");
        }

        try
        {
            float heightLNode = heightmap[LLNodeX, LLNodeZ];
            float heightUNode = heightmap[LLNodeX, LLNodeZ + 1];
            float heightLRRNode = heightmap[LLNodeX + 1, LLNodeZ];
            float heightURRNode = heightmap[LLNodeX + 1, LLNodeZ + 1];
        }
        catch
        {
            Debug.Log(droplet.x + ", " + droplet.z + ", " + droplet.stepsTaken);
        }

        float heightLLNode = heightmap[LLNodeX, LLNodeZ];
        float heightULNode = heightmap[LLNodeX, LLNodeZ + 1];
        float heightLRNode = heightmap[LLNodeX + 1, LLNodeZ];
        float heightURNode = heightmap[LLNodeX + 1, LLNodeZ + 1];

        float xGradOld = (heightLRNode - heightLLNode) * (1 - zPosOff) + (heightURNode - heightULNode) * zPosOff;
        float zGradOld = (heightULNode - heightLLNode) * (1 - xPosOff) + (heightURNode - heightLRNode) * xPosOff;

        float oldHeight = CalculateHeight(droplet);

        droplet.directionX = droplet.directionX * inertia - xGradOld * (1 - inertia);
        droplet.directionZ = droplet.directionZ * inertia - zGradOld * (1 - inertia);

        float oldX = droplet.x;
        float oldZ = droplet.z;

        droplet.x += droplet.directionX;
        droplet.z += droplet.directionZ;

        if (droplet.x < 0 || droplet.x >= xSize || droplet.z < 0 || droplet.z >= zSize)
        {
            return;
        }
        float newHeight = CalculateHeight(droplet);
        float heightDiff = newHeight - oldHeight;

        //Reread this section.
        if (heightDiff > 0)
        {
            //Deposit, fill pit until full or all carried sediment is dropped
            float dropAmount = Mathf.Min(heightDiff, droplet.sediment);
            DepositTerrain(oldX, oldZ, dropAmount);
            droplet.sediment -= dropAmount;
        }
        else
        {
            //Calculate new carry amount, do other stuff
            droplet.capacity = Mathf.Max(-heightDiff, pMinSlope) * droplet.velocity * droplet.water * pCapacity;
            if (droplet.sediment > droplet.capacity)
            {
                //Drop a percentage of sediment surplus defined by pDeposition at position pOld
                float dropAmount = (droplet.sediment - droplet.capacity) * pDeposition;
                DepositTerrain(oldX, oldZ, dropAmount);
                droplet.sediment -= dropAmount;
            }
            else
            {
                //Take a percentage of remaining capacity from surroundings as defined by pErosion at position pOld
                //MAKE SURE TO NEVER take more sediment than height difference
                float takeAmount = Mathf.Min((droplet.capacity - droplet.sediment) * pErosion, -heightDiff);
                ErodeTerrain(oldX, oldZ, takeAmount);
                droplet.sediment += takeAmount;
            }
        }

        //Is this math correct?
        droplet.velocity = Mathf.Sqrt(Mathf.Pow(droplet.velocity, 2) - heightDiff * pGravity);
        droplet.water = droplet.water * (1 - pEvaporation);

        //Repeat these steps until run out of map or die in a pit
        if (droplet.sediment == 0)
        {
            return;
        }
        droplet.stepsTaken++;
        SimulateDropletStep(droplet);
    }

    void ErodeTerrain(float posX, float posZ, float takeAmount)
    {
        Dictionary<int, float> nodes = CalculateErosionWeights(posX, posZ);
        foreach (KeyValuePair<int, float> node in nodes)
        {
            float newY = Mathf.Max(vertices[node.Key].y - node.Value * takeAmount, 0);
            vertices[node.Key].y = newY;
            int z = node.Key / (xSize + 1);
            int x = node.Key % (xSize + 1);
            heightmap[x, z] = newY;
        }
    }

    void DepositTerrain(float posX, float posZ, float dropAmount)
    {
        int LLNodeX = (int) posX;
        int LLNodeZ = (int) posZ;
        float xPosOff = posX - LLNodeX;
        float zPosOff = posZ - LLNodeZ;
        int LLVertice = LLNodeX + (xSize + 1) * LLNodeZ;

        float LLYVal = heightmap[LLNodeX, LLNodeZ] + dropAmount * (1 - xPosOff) * (1 - zPosOff);
        float LRYVal = heightmap[LLNodeX + 1, LLNodeZ] + dropAmount * xPosOff * (1 - zPosOff);
        float ULYVal = heightmap[LLNodeX, LLNodeZ + 1] + dropAmount * (1 - xPosOff) * zPosOff;
        float URYVal = heightmap[LLNodeX + 1, LLNodeZ + 1] + dropAmount * xPosOff * zPosOff;

        vertices[LLVertice].y = LLYVal;
        heightmap[LLNodeX, LLNodeZ] = LLYVal;

        vertices[LLVertice + 1].y = LRYVal;
        heightmap[LLNodeX + 1, LLNodeZ] = LRYVal;

        vertices[LLVertice + xSize + 1].y = ULYVal;
        heightmap[LLNodeX, LLNodeZ + 1] = ULYVal;

        vertices[LLVertice + xSize + 2].y = URYVal;
        heightmap[LLNodeX + 1, LLNodeZ + 1] = URYVal;
    }

    Dictionary<int, float> CalculateErosionWeights(float posX, float posZ)
    {
        Dictionary<int, float> tempNodes = new Dictionary<int, float>();
        Dictionary<int, float> nodeWeights = new Dictionary<int, float>();

        int minZ = Mathf.Max(0, Mathf.CeilToInt(posZ - pRadius));
        int maxZ = Mathf.Min(zSize, (int)(posZ + pRadius));
        int minX = Mathf.Max(0, Mathf.CeilToInt(posX - pRadius));
        int maxX = Mathf.Min(xSize, (int)(posX + pRadius));

        float weightSum = 0;
        for (int z = minZ; z <= maxZ; z++)
        {
            for (int x = minX; x < maxX; x++)
            {
                //Create dictionary of all nodes within radius (WORK IN PROGRESS)

                //Is this right?
                float weight = Mathf.Max(0, pRadius - Mathf.Sqrt(Mathf.Pow(x - posX, 2) + Mathf.Pow(z - posZ, 2)));
                weightSum += weight;
                tempNodes.Add(z * (xSize + 1) + x + 1, weight);
            }
        }
        foreach (KeyValuePair<int, float> entry in tempNodes)
        {
            nodeWeights.Add(entry.Key, entry.Value / weightSum);
        }
        return nodeWeights;
    }

    float CalculateHeight(Droplet droplet)
    {
        int LLNodeX = (int)droplet.x;
        int LLNodeZ = (int)droplet.z;

        float xPosOff = droplet.x - LLNodeX;
        float zPosOff = droplet.z - LLNodeZ;

        try
        {
            float heightLLNode = heightmap[LLNodeX, LLNodeZ];
            float heightULNode = heightmap[LLNodeX, LLNodeZ + 1];
            float heightLRNode = heightmap[LLNodeX + 1, LLNodeZ];
            float heightURNode = heightmap[LLNodeX + 1, LLNodeZ + 1];
            float bilinearBot = (1 - xPosOff) * heightLLNode + xPosOff * heightLRNode;
            float bilinearTop = (1 - xPosOff) * heightULNode + xPosOff * heightURNode;
            float heightDroplet = (1 - zPosOff) * bilinearBot + zPosOff * bilinearTop;
            return heightDroplet;
        }
        catch
        {
            Debug.Log(droplet.x + ", " + droplet.z + ", " + droplet.stepsTaken);
        }
        return 0;
    }

    class Droplet
    {
        public float x;
        public float z;
        public float directionX;
        public float directionZ;
        public int stepsTaken;
        public float sediment;
        public float velocity;
        public float water;
        public float capacity;

        public Droplet(float dropX, float dropZ)
        {
            x = dropX;
            z = dropZ;
            directionX = 0;
            directionZ = 0;
            stepsTaken = 0;

            //Initialize these values
            sediment = 0;
            velocity = 1;
            water = 1;
            capacity = 0;
        }
    }

    void SimulateErosion(int droplets)
    {
        for (int i = 0; i < droplets; i++)
        {
            float posX = (float) (rand.NextDouble() * xSize);
            float posZ = (float) (rand.NextDouble() * zSize);
            SimulateDropletStep(new Droplet(posX, posZ));
        }
    }

    // Update is called once per frame
    void Update()
    {
        
    }
}
