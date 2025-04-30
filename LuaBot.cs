using System.IO;
using UnityEngine;
using System.Reflection;
using System.Collections;
using MoonSharp.Interpreter;
using System.Collections.Generic;

public class LuaBot : MonoBehaviour
{
    public int id;
    public bool running;
    private bool moving;
    private bool delaying;
    private float delayTime;
    public bool msgAvailable;
    public string incomingMessage;
    public string networkSound = "";
    public string display = "output";
    public string fileName = "help.lua";
    public string outputDisplay = "[Output]\n";
    public string program = "FCE OS V 0.0001 A";
    public string inventoryDisplay = "[Inventory]\n";

    public Vector3 lookDir;
    private Vector3 moveDir;
    public Vector3 startPosition;

    public List<ItemBase> inventory;

    public AudioClip digSound;
    public AudioClip buildSound;
    public AudioClip robotSound;

    private UnityEngine.Coroutine mainCoroutine;
    private UnityEngine.Coroutine moveCoroutine;
    private UnityEngine.Coroutine delayCoroutine;

    private delegate void Action();
    private delegate void Action<T>(T t);
    private delegate void Action<T, U, V>(T t, U u, V v);
    private delegate TResult Func<out TResult> ();
    private delegate TResult Func<in T1, out TResult> (T1 arg1);
    private delegate TResult Func<in T1, in T2, out TResult> (T1 arg1, T2 arg2);
    private delegate TResult Func<in T1, in T2, in T3, out TResult> (T1 arg1, T2 arg2, T3 arg3);

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

    // Returns true if the cube at the given coordinates is passable.
    private bool IsPassable(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        Vector3 checkPos = transform.position + new Vector3(x, y, z);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(checkPos, out long cX, out long cY, out long cZ);
        ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
        return CubeHelper.IsTypeConsideredPassable(localCube);
    }

    private bool CanMove()
    {
        Vector3 pos = transform.position;
        lookDir = new Vector3(pos.x + moveDir.x, pos.y, pos.z + moveDir.z);
        transform.LookAt(lookDir);
        Vector3 newPos = pos + moveDir;
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(newPos, out long cX, out long cY, out long cZ);
        ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
        return CubeHelper.IsTypeConsideredPassable(localCube);
    }

    // Move function called by lua scripts.
    private void Move(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        moveDir = new Vector3(x, y, z);
        moving = true;
    }

    // Digs a cube and places it in the robot's inventory.
    private void Dig(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        lookDir = new Vector3(transform.position.x + x, transform.position.y, transform.position.z + z);
        transform.LookAt(lookDir);
        Vector3 digPos = transform.position + new Vector3(x, y, z);
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
                    if (!gathered)
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

    // Places a cube from the robot's inventory.
    private void Build(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        lookDir = new Vector3(transform.position.x + x, transform.position.y, transform.position.z + z);
        transform.LookAt(lookDir);
        Vector3 buildPos = transform.position + new Vector3(x, y, z);
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

    // Hopper function called by lua scripts.
    private void TakeFromHopper(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        lookDir = new Vector3(transform.position.x + x, transform.position.y, transform.position.z + z);
        transform.LookAt(lookDir);
        Vector3 hopperPos = transform.position + new Vector3(x, y, z);
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
                        if (item != null)
                        {
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
    }

    // Adds items to a hopper.
    private void EmptyToHopper(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        lookDir = new Vector3(transform.position.x + x, transform.position.y, transform.position.z + z);
        transform.LookAt(lookDir);
        Vector3 hopperPos = transform.position + new Vector3(x, y, z);
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
                        bool addedItem = false;
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
                                        addedItem = true;
                                    }
                                }
                                else if (inventory[i].mType == ItemType.ItemStack)
                                {
                                    ItemStack originalStack = (ItemStack)inventory[i];
                                    ItemStack splitStack = new ItemStack(originalStack.mnItemID, freeSpace);
                                    if (storageMachineInterface.TryInsert(null, splitStack))
                                    {
                                        inventory[i].DecrementStack(freeSpace);
                                        addedItem = true;
                                    }
                                }
                            }
                            else if (storageMachineInterface.TryInsert(null, inventory[i]))
                            {
                                inventory.Remove(inventory[i]);
                                addedItem = true;
                            }
                        }
                        if (addedItem == true)
                        {
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
    }

    // Gets the amount of an item in the robot's inventory.
    private int GetInventory(string itemName)
    { 
        foreach (ItemBase item in inventory)
        {
            if (item.GetName() == itemName)
            {
                return item.GetAmount();
            }
        }
        return 0;
    }

    // Harvests seeds from hydroponics bays.
    private void Harvest(int x, int y, int z)
    {
        Mathf.Clamp(x, -1, 1);
        Mathf.Clamp(y, -1, 1);
        Mathf.Clamp(z, -1, 1);
        if (WorldScript.mbIsServer)
        {
            lookDir = new Vector3(transform.position.x + x, transform.position.y, transform.position.z + z);
            transform.LookAt(lookDir);
            Vector3 harvestPos = transform.position + new Vector3(x, y, z);
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
                            if (!gathered)
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

    // Returns the current power of a PowerStorageInterface or PowerConsumerInterface.
    private int GetPower(int uX, int uY, int uZ)
    {
        if (WorldScript.mbIsServer)
        {
            lookDir = new Vector3(transform.position.x + uX, transform.position.y, transform.position.z + uZ);
            transform.LookAt(lookDir);
            Vector3 psbPos = transform.position + new Vector3(uX, uY, uZ);
            WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(psbPos, out long cX, out long cY, out long cZ);
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
                    SegmentEntity entity = segment.SearchEntity(cX, cY, cZ);
                    if (entity == null)
                    {
                         return -1;
                    }
                    if (entity.mbDelete)
                    {
                         return -2;
                    }
                    if (entity is PowerStorageInterface psi)
                    {
                        return (int)psi.CurrentPower;
                    }
                    if (entity is PowerConsumerInterface pci)
                    {
                        return (int)(pci.GetMaxPower() - pci.GetRemainingPowerCapacity());
                    }
                    return -3;
                }
                return -4;
            }
            return -5;
        }
        return -6;
    }

    // Returns the state of an ore extractor.
    private string GetExtractorState(int uX, int uY, int uZ)
    {
        Dictionary<OreExtractor.eState, string> stateDict = new Dictionary<OreExtractor.eState, string>
        {
            { OreExtractor.eState.eDrillStuck, "Stuck" },
            { OreExtractor.eState.eFetchingEntryPoint, "Fetching Entry Point" },
            { OreExtractor.eState.eFetchingExtractionPoint, "Fetching Extraction Point" },
            { OreExtractor.eState.eIdle, "Idle" },
            { OreExtractor.eState.eMining, "Mining" },
            { OreExtractor.eState.eOutOfPower, "Out of Power" },
            { OreExtractor.eState.eOutOfPowerVeinDepleted, "Out of Power / Vein Depleted" },
            { OreExtractor.eState.eOutOfStorage, "Out of Storage" },
            { OreExtractor.eState.eOutOfStorageVeinDepleted, "Out of Storage / Vein Depleted" },
            { OreExtractor.eState.eSearchingForOre, "Searching for Ore" },
            { OreExtractor.eState.eVeinDepleted, "Vein Depleted" }
        };
 
        if (WorldScript.mbIsServer)
        {
            lookDir = new Vector3(transform.position.x + uX, transform.position.y, transform.position.z + uZ);
            transform.LookAt(lookDir);
            Vector3 psbPos = transform.position + new Vector3(uX, uY, uZ);
            WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(psbPos, out long cX, out long cY, out long cZ);
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
                    if (segment.FetchEntity(eSegmentEntity.OreExtractor, cX, cY, cZ) is OreExtractor ex)
                    {
                        return stateDict[ex.meState];
                    }
                    return "Extractor Error";
                }
                return "Bad Segment Error";
            }
            return "Null Segment Error";
        }
        return "WorldScript Error";
    }

    // Sends a message to another bot.
    private bool Transmit(int botID, string data)
    {
        LuaBot[] robots = FindObjectsOfType<LuaBot>();
        foreach (LuaBot robot in robots)
        {
            if (botID == robot.id && !robot.msgAvailable)
            {
                robot.msgAvailable = true;
                robot.incomingMessage = data;
                return true;
            }
        }
        return false;
    }
 
    // Receives a message from another bot.
    private string Receive()
    {
        if (msgAvailable)
        {
            msgAvailable = false;
            return incomingMessage;
        }
        return "No message available!";
    }

    // Gets the robot's id number.
    private int GetID()
    {
        return id;
    }

    // Prints a list of all available scripts.
    private void GetScripts()
    {
        string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        string scriptFolder = Path.Combine(assemblyFolder, "Scripts");
        Directory.CreateDirectory(scriptFolder);
        DirectoryInfo dinfo = new DirectoryInfo(scriptFolder);
        foreach (FileInfo file in dinfo.GetFiles("*.lua"))
        {
            Print(file.Name);
        }
    }

    // Sends a chat message.
    private void Chat(string message)
    {
        if (NetworkManager.instance.mServerThread != null)
        {
            ChatLine chatLine = new ChatLine
            {
                mPlayer = -1,
                mPlayerName = "[SERVER]",
                mText = "Lua Bot " + id + ": " + message,
                mType = ChatLine.Type.Normal
            };
            NetworkManager.instance.QueueChatMessage(chatLine);
        }
    }

    // Prints text to the output window.
    private void Print(string line)
    {
        if (outputDisplay.Length >= 10000)
        {
            outputDisplay = "[Output]\n";
        }
        outputDisplay += line + "\n";
    }

    // Pauses execution.
    private void Delay(float seconds)
    {
        delaying = true;
        delayTime = seconds;
    }

    // Runs the robot's lua program.
    public void RunScript()
    {
        mainCoroutine = StartCoroutine(RunScriptEnum());
    }

    // Runs the robot's lua program.
    private IEnumerator RunScriptEnum()
    {
        if (!running && program.Contains("function main()"))
        {
            running = true;
            Print("Loading script...");
            string scriptCode = @"" + program;
            Script script = new Script(CoreModules.None);
            script.Globals["Move"] = (Action<int, int, int>)Move;
            script.Globals["Dig"] = (Action<int, int, int>)Dig;
            script.Globals["Build"] = (Action<int, int, int>)Build;
            script.Globals["Harvest"] = (Action<int, int, int>)Harvest;
            script.Globals["IsPassable"] = (Func<int, int, int, bool>)IsPassable;
            script.Globals["TakeFromHopper"] = (Action<int, int, int>)TakeFromHopper;
            script.Globals["EmptyToHopper"] = (Action<int, int, int>)EmptyToHopper;
            script.Globals["GetInventory"] = (Func<string, int>)GetInventory;
            script.Globals["GetPower"] = (Func<int, int, int, int>)GetPower;
            script.Globals["GetExtractorState"] = (Func<int, int, int, string>)GetExtractorState;
            script.Globals["Transmit"] = (Func<int, string, bool>)Transmit;
            script.Globals["Receive"] = (Func<string>)Receive;
            script.Globals["GetScripts"] = (Action)GetScripts;
            script.Globals["GetID"] = (Func<int>)GetID;
            script.Globals["Chat"] = (Action<string>)Chat;
            script.Globals["Print"] = (Action<string>)Print;
            script.Globals["Delay"] = (Action<float>)Delay;

            try
            {
                script.DoString(scriptCode);
            }
            catch (System.Exception e)
            {
                Print(e.Message);
            }

            DynValue function = script.Globals.Get("main");

            if (function != null)
            {
                DynValue coroutine = script.CreateCoroutine(function);
                if (coroutine != null)
                {
                    Print("Starting main coroutine.");
                    coroutine.Coroutine.AutoYieldCounter = 1;

                    DynValue result = coroutine.Coroutine.Resume();
                    while (running && result.Type == DataType.YieldRequest)
                    {
                        float insTime = 0.05f - Time.deltaTime;
                        insTime = Mathf.Clamp(insTime, 0.0f, 0.05f);
                        yield return new WaitForSeconds(insTime);
                        if (delaying)
                        {
                            yield return new WaitForSeconds(delayTime);
                            delaying = false;
                        } 
                        else if (moving)
                        {
                            if (CanMove())
                            {
                                Vector3 newPos = transform.position + moveDir;
                                for (int i = 0; i < 8; i++)
                                {
                                    float x = moveDir.x * 0.125f;
                                    float y = moveDir.y * 0.125f;
                                    float z = moveDir.z * 0.125f;
                                    transform.position += new Vector3(x, y, z);
                                    float moveTime = 0.03125f - Time.deltaTime;
                                    moveTime = Mathf.Clamp(moveTime, 0.0f, 0.03125f);
                                    yield return new WaitForSeconds(moveTime);
                                }
                                transform.position = newPos;
                            }
                            moving = false;
                        }
                        result = coroutine.Coroutine.Resume();
                    }
                }
                else
                {
                    Print("Failed to create coroutine.");
                }
            }
        }
        else
        {
            Print("Missing main function.");
        }

        yield return null;
        running = false;
    }
}
