using UnityEngine;
using System.Collections;
using MoonSharp.Interpreter;
using System.Collections.Generic;

public class Robot : MonoBehaviour
{
    public string program = "FCE OS V 0.0001 A";
    public string fileName = "help.lua";
    public int id;
    public string networkSound = "";
    public Vector3 startPosition;
    public List<ItemBase> inventory;
    public string inventoryDisplay = "[Inventory]\n";
    public AudioClip digSound;
    public AudioClip buildSound;
    public AudioClip robotSound;
    private float moveCount;
    private float digCount;
    private float buildCount;
    private float harvestCount;
    private float takeFromHopperCount;
    private float emptyToHopperCount;
    private UnityEngine.Coroutine moveCoroutine;
    private UnityEngine.Coroutine digCoroutine;
    private UnityEngine.Coroutine buildCoroutine;
    private UnityEngine.Coroutine harvestCoroutine;
    private UnityEngine.Coroutine takeFromHopperCoroutine;
    private UnityEngine.Coroutine emptyToHopperCoroutine;
    private delegate void Action();
    private delegate void Action<T>(T t);
    private delegate void Action<T, U>(T t, U u);
    private delegate void Action<T, U, V>(T t, U u, V v);
    private delegate TResult Func<TResult>();
    private delegate TResult Func<T, TResult>(T t);
    private delegate TResult Func<T, U, TResult>(T t, U u);
    private delegate TResult Func<T, U, V, TResult>(T t, U u, V v);

    // Initialization.
    public IEnumerator Start()
    {
        yield return null;
    }

    // Called once per frame by unity engine.
    public void Update()
    {
        if (NetworkManager.instance.mServerThread == null)
        {
            if (!GetComponent<AudioSource>().isPlaying)
            {
                GetComponent<AudioSource>().clip = robotSound;
                GetComponent<AudioSource>().spatialize = true;
                GetComponent<AudioSource>().spatialBlend = 1.0f;
                GetComponent<AudioSource>().rolloffMode = AudioRolloffMode.Linear;
                GetComponent<AudioSource>().maxDistance = 10;
                GetComponent<AudioSource>().loop = true;
                GetComponent<AudioSource>().Play();
            }
            transform.Rotate(Vector3.up * 50 * Time.deltaTime);
        }
    }

    // Updates the robot's inventory.
    public void UpdateInventory()
    {
        inventoryDisplay = "[Inventory]\n";
        foreach(ItemBase item in inventory)
        {
            inventoryDisplay += item.GetName() + ": " + item.GetAmount() + "\n";
        }
    }

    // Moves the robot.
    private IEnumerator MoveEnum(int x, int y, int z)
    {
        moveCount += 0.5f;
        yield return new WaitForSeconds(moveCount);
        Vector3 newPos = transform.position + new Vector3(x, y, z);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(newPos, out long cX, out long cY, out long cZ);
        ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
        if (CubeHelper.IsTypeConsideredPassable(localCube))
        {
            for (int i = 0; i < 4; i++)
            {
                transform.position += new Vector3(x * 0.25f, y * 0.25f, z * 0.25f);
                yield return new WaitForSeconds(0.01f);
            }
            transform.position = newPos;
        }
    }

    // Digs a cube below the robot.
    private IEnumerator DigEnum(int uX, int uY, int uZ)
    {
        digCount += 0.5f;
        yield return new WaitForSeconds(digCount);
        Vector3 digPos = transform.position + new Vector3(uX, uY, uZ);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(digPos, out long cX, out long cY, out long cZ);
        if (NetworkManager.instance.mServerThread != null)
        {
            cX = cX - 64;
            cY = cY - 64;
            cZ = cZ - 64;
        }
        Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
        if (segment != null)
        {
            if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
            {
                ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
                ushort localCubeValue = WorldScript.instance.GetCubeValue(cX, cY, cZ);
                if (localCube != eCubeTypes.Air)
                {
                    int digAmount = 1;
                    if (CubeHelper.IsOre(localCube))
                    {
                        int orePerCube = DifficultySettings.mbCasualResource ? 2560 : 1280;
                        digAmount = (int)(orePerCube * 0.05f);
                    }
                    ItemCubeStack stack = new ItemCubeStack(localCube, localCubeValue, digAmount);
                    bool gathered = false;
                    foreach (ItemBase item in inventory)
                    {
                        if (item.GetName() == stack.GetName())
                        {
                            item.IncrementStack(digAmount);
                            gathered = true;
                            break;
                        }
                    }
                    if (gathered == false)
                    {
                        inventory.Add(stack);
                    }
                    WorldScript.instance.BuildFromEntity(segment, cX, cY, cZ, eCubeTypes.Air);
                    if (digSound != null)
                    {
                        AudioSource.PlayClipAtPoint(digSound, transform.position);
                    }
                    networkSound = "dig";
                    UpdateInventory();
                }
            }
        }
    }

    // Places a cube below the robot.
    private IEnumerator BuildEnum(int uX, int uY, int uZ)
    {
        buildCount += 0.5f;
        yield return new WaitForSeconds(buildCount);
        Vector3 buildPos = transform.position + new Vector3(uX, uY, uZ);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(buildPos, out long cX, out long cY, out long cZ);
        if (NetworkManager.instance.mServerThread != null)
        {
            cX = cX - 64;
            cY = cY - 64;
            cZ = cZ - 64;
        }
        Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
        if (segment != null)
        {
            if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
            {
                ItemCubeStack buildItem = null;
                foreach (ItemBase item in inventory)
                {
                    if (item.mType == ItemType.ItemCubeStack)
                    {
                        buildItem = (ItemCubeStack)item;
                    }
                }
                if (buildItem != null)
                {
                    WorldScript.instance.BuildFromEntity(segment, cX, cY, cZ, buildItem.mCubeType);
                    if (buildItem.mnAmount > 1)
                    {
                        buildItem.DecrementStack(1);
                    }
                    else
                    {
                        inventory.Remove(buildItem);
                    }
                    if (buildSound != null)
                    {
                        AudioSource.PlayClipAtPoint(buildSound, transform.position);
                    }
                    networkSound = "build";
                    UpdateInventory();
                }
            }
        }
    }

    // Harvests seeds from hydroponics bays.
    private IEnumerator HarvestEnum(int uX, int uY, int uZ)
    {
        harvestCount += 0.5f;
        yield return new WaitForSeconds(harvestCount);
        if (WorldScript.mbIsServer)
        {
            Vector3 harvestPos = transform.position + new Vector3(uX, uY, uZ);
            WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(harvestPos, out long cX, out long cY, out long cZ);
            if (NetworkManager.instance.mServerThread != null)
            {
                cX = cX - 64;
                cY = cY - 64;
                cZ = cZ - 64;
            }
            Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
            if (segment != null)
            {
                if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
                {
                    if (segment.FetchEntity(eSegmentEntity.Hydroponics, cX, cY, cZ) is HydroponicsBay hydroponicsBay)
                    {
                        if (hydroponicsBay.HasPlant && hydroponicsBay.mPlant != null && hydroponicsBay.mPlant.mbReadyForHarvest)
                        {
                            ItemBase plant = hydroponicsBay.mPlant.Collect();
                            bool gathered = false;
                            foreach (ItemBase item in inventory)
                            {
                                if (item.GetName() == plant.GetName())
                                {
                                    item.IncrementStack(1);
                                    gathered = true;
                                    break;
                                }
                            }
                            if (gathered == false)
                            {
                                inventory.Add(plant);
                            }
                            if (digSound != null)
                            {
                                AudioSource.PlayClipAtPoint(digSound, transform.position);
                            }
                            networkSound = "dig";
                            UpdateInventory();
                        }
                    }
                }
            }
        }
    }

    // Takes items from a hopper.
    private IEnumerator TakeFromHopperEnum(int uX, int uY, int uZ)
    {
        takeFromHopperCount += 0.5f;
        yield return new WaitForSeconds(takeFromHopperCount);
        Vector3 hopperPos = transform.position + new Vector3(uX, uY, uZ);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(hopperPos, out long cX, out long cY, out long cZ);
        if (NetworkManager.instance.mServerThread != null)
        {
            cX = cX - 64;
            cY = cY - 64;
            cZ = cZ - 64;
        }
        if (WorldScript.mbIsServer)
        {
            Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
            if (segment != null)
            {
                if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
                {
                    if (segment.SearchEntity(cX, cY, cZ) is StorageMachineInterface storageMachineInterface)
                    {
                        storageMachineInterface.TryExtractAny(null, 9999, out ItemBase item);
                        inventory.Add(item);
                        if (digSound != null)
                        {
                            AudioSource.PlayClipAtPoint(digSound, transform.position);
                        }
                        networkSound = "dig";
                        UpdateInventory();
                    }
                }
            }
        }
    }

    // Adds items to a hopper.
    private IEnumerator EmptyToHopperEnum(int uX, int uY, int uZ)
    {
        emptyToHopperCount += 0.5f;
        yield return new WaitForSeconds(emptyToHopperCount);
        Vector3 hopperPos = transform.position + new Vector3(uX, uY, uZ);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(hopperPos, out long cX, out long cY, out long cZ);
        if (NetworkManager.instance.mServerThread != null)
        {
            cX = cX - 64;
            cY = cY - 64;
            cZ = cZ - 64;
        }
        if (WorldScript.mbIsServer)
        {
            Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
            if (segment != null)
            {
                if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
                {
                    if (segment.SearchEntity(cX, cY, cZ) is StorageMachineInterface storageMachineInterface)
                    {
                        for (int i = 0; i < inventory.Count; i++)
                        {
                            int freeSpace = storageMachineInterface.RemainingCapacity;
                            if (inventory[i].GetAmount() > freeSpace)
                            {
                                if (inventory[i].mType == ItemType.ItemCubeStack)
                                {
                                    ItemCubeStack originalStack = (ItemCubeStack)inventory[i];
                                    ItemCubeStack splitStack = new ItemCubeStack(originalStack.mCubeType, originalStack.mCubeValue, freeSpace);
                                    if (storageMachineInterface.TryInsert(null, splitStack))
                                    {
                                        inventory[i].DecrementStack(freeSpace);
                                    }
                                }
                                else if (inventory[i].mType == ItemType.ItemStack)
                                {
                                    ItemStack originalStack = (ItemStack)inventory[i];
                                    ItemStack splitStack = new ItemStack(originalStack.mnItemID, freeSpace);
                                    if (storageMachineInterface.TryInsert(null, splitStack))
                                    {
                                        inventory[i].DecrementStack(freeSpace);
                                    }
                                }
                            }
                            else if (storageMachineInterface.TryInsert(null, inventory[i]))
                            {
                                inventory.Remove(inventory[i]);
                            }
                        }
                        if (buildSound != null)
                        {
                            AudioSource.PlayClipAtPoint(buildSound, transform.position);
                        }
                        networkSound = "build";
                        UpdateInventory();
                    }
                }
            }
        }
    }

    // Move function called by lua scripts.
    private void Move(int x, int y, int z)
    {
        x = x > 1 ? 1 : x;
        y = y > 1 ? 1 : y;
        z = z > 1 ? 1 : z;
        moveCoroutine = StartCoroutine(MoveEnum(x, y, z));
    }

    // Dig function called by lua scripts.
    private void Dig(int x, int y, int z)
    {
        digCoroutine = StartCoroutine(DigEnum(x, y, z));
    }

    // Build function called by lua scripts.
    private void Build(int x, int y, int z)
    {
        buildCoroutine = StartCoroutine(BuildEnum(x, y, z));
    }

    // Harvest function called by lua scripts.
    private void Harvest(int x, int y, int z)
    {
        harvestCoroutine = StartCoroutine(HarvestEnum(x, y, z));
    }

    // Hopper function called by lua scripts.
    private void TakeFromHopper(int x, int y, int z)
    {
        takeFromHopperCoroutine = StartCoroutine(TakeFromHopperEnum(x, y, z));
    }

    // Hopper function called by lua scripts.
    private void EmptyToHopper(int x, int y, int z)
    {
        emptyToHopperCoroutine = StartCoroutine(EmptyToHopperEnum(x, y, z));
    }

    // Runs the robot's lua program.
    public void RunScript()
    {
        string scriptCode = @"" + program; 

        Script script = new Script();

        script.Globals["Move"] = (Action<int, int, int>)Move;

        script.Globals["Dig"] = (Action<int, int, int>)Dig;

        script.Globals["Build"] = (Action<int, int, int>)Build;

        script.Globals["Harvest"] = (Action<int, int, int>)Harvest;

        script.Globals["TakeFromHopper"] = (Action<int, int, int>)TakeFromHopper;

        script.Globals["EmptyToHopper"] = (Action<int, int, int>)EmptyToHopper;

        script.DoString(scriptCode);

        moveCount = 0;
        digCount = 0;
        buildCount = 0;
        harvestCount = 0;
        takeFromHopperCount = 0;
        emptyToHopperCount = 0;
    }
}
