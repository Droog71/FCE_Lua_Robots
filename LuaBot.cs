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

    private UnityEngine.Coroutine luaScriptCoroutine;

    private delegate void Action();
    private delegate void Action<T>(T t);
    private delegate void Action<T, U, V>(T t, U u, V v);
    private delegate void Action<T, U, V, W>(T t, U u, V v ,W w);
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

    // Gets the coordinates of the cube the robot is focused on.
    private void GetCubeCoords(int x, int y, int z, out long cX, out long cY, out long cZ)
    {
        x = Mathf.Clamp(x, -1, 1);
        y = Mathf.Clamp(y, -1, 1);
        z = Mathf.Clamp(z, -1, 1);
        Vector3 pos = transform.position;
        lookDir = new Vector3(pos.x + x, pos.y, pos.z + z);
        transform.LookAt(lookDir);
        Vector3 cubePos = transform.position + new Vector3(x, y, z);
        WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(cubePos, out long fX, out long fY, out long fZ);
        bool server = NetworkManager.instance.mServerThread != null;
        cX = server? fX - 64 : fX;
        cY = server? fY - 64 : fY;
        cZ = server? fZ - 64 : fZ;
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
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
        return CubeHelper.IsTypeConsideredPassable(localCube);
    }

    // Returns true if the cube at the given coordinates is ore.
    private bool IsOre(int x, int y, int z)
    {
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
        return CubeHelper.IsOre(localCube);
    }

    // Move function called by lua scripts.
    private void Move(int x, int y, int z)
    {
        if (IsPassable(x, y, z))
        {
            moveDir = new Vector3(x, y, z);
            moving = true;
        }
    }

    // Digs a cube and places it in the robot's inventory.
    private void Dig(int x, int y, int z)
    {
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
        if (segment != null)
        {
            if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
            {
                ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
                ushort localCubeValue = WorldScript.instance.GetCubeValue(cX, cY, cZ);
                if (localCube != eCubeTypes.Air)
                {
                    ItemCubeStack stack = new ItemCubeStack(localCube, localCubeValue, 1);
                    bool gathered = false;
                    foreach (ItemBase item in inventory)
                    {
                        if (item.GetName() == stack.GetName())
                        {
                            item.IncrementStack(1);
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

    // Mines ore.
    private void Mine(int x, int y, int z)
    {
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
        if (segment != null)
        {
            if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
            {
                ushort localCube = WorldScript.instance.GetLocalCube(cX, cY, cZ);
                if (CubeHelper.IsOre(localCube))
                {
                    int orePerCube = DifficultySettings.mbCasualResource ? 2560 : 1280;
                    int oreInCube = segment.GetCubeData(cX, cY, cZ).mValue;
                    int digAmount = (int)(orePerCube * 0.1f);
                    int oreRemaining = oreInCube - digAmount;
                    int oreCollected = digAmount;
                    if (oreRemaining < digAmount)
                    {
                        oreCollected = oreRemaining;
                        WorldScript.instance.BuildFromEntity(segment, cX, cY, cZ, eCubeTypes.Air);
                    }
                    else
                    {
                        ushort oreAfterDig = (ushort)(oreRemaining - oreCollected);
                        WorldScript.instance.SetCubeValue(cX, cY, cZ, oreAfterDig);
                        int newTexture = TerrainData.GetSideTexture(localCube, oreAfterDig);
                        int oldTexture = TerrainData.GetSideTexture(localCube, (ushort)oreInCube);
                        if (newTexture != oldTexture)
                        {
                            segment.RequestRegenerateGraphics();
                        }
                    }
                    ushort localCubeValue = WorldScript.instance.GetCubeValue(cX, cY, cZ);
                    ItemCubeStack stack = new ItemCubeStack(localCube, localCubeValue, oreCollected / 16);
                    bool gathered = false;
                    foreach (ItemBase item in inventory)
                    {
                        if (item.GetName() == stack.GetName())
                        {
                            item.IncrementStack(oreCollected / 16);
                            gathered = true;
                            break;
                        }
                    }
                    if (!gathered)
                    {
                        inventory.Add(stack);
                    }
                    if (digSound != null)
                    {
                        AudioSource.PlayClipAtPoint(digSound, transform.position);
                    }
                    networkSound = "dig";
                    UpdateInventory();
                    Delay(1);
                }
            }
        }
    }

    // Places a cube from the robot's inventory.
    private void Build(int x, int y, int z, string cubeName = "")
    {
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
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
                        if (cubeName == "" || item.GetName() == cubeName)
                        {
                            buildItem = (ItemCubeStack)item;
                            break;
                        }
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
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
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
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
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
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        if (WorldScript.mbIsServer)
        {
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
    private int GetPower(int x, int y, int z)
    {
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        if (WorldScript.mbIsServer)
        {
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

    // Dictionary for converting ore extractor state to string.
    private readonly Dictionary<OreExtractor.eState, string> extractorStateDict = new Dictionary<OreExtractor.eState, string>
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

    // Returns the state of an ore extractor.
    private string GetExtractorState(int x, int y, int z)
    {
        GetCubeCoords(x, y, z, out long cX, out long cY, out long cZ);
        if (WorldScript.mbIsServer)
        {
            Segment segment = WorldScript.instance.GetSegment(cX, cY, cZ);
            if (segment != null)
            {
                if (segment.mbInitialGenerationComplete && !segment.mbDestroyed)
                {
                    if (segment.FetchEntity(eSegmentEntity.OreExtractor, cX, cY, cZ) is OreExtractor ex)
                    {
                        return extractorStateDict[ex.meState];
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
    public void Print(string line)
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
        if (!running)
        {
            running = true;
            luaScriptCoroutine = StartCoroutine(RunScriptEnum());
        }
    }

    // Runs the robot's lua program.
    private IEnumerator RunScriptEnum()
    {
        if (program.Contains("function main()"))
        {
            Print("Loading script...");
            string scriptCode = @"" + program;
            Script script = new Script(CoreModules.None);
            script.Globals["Move"] = (Action<int, int, int>)Move;
            script.Globals["Dig"] = (Action<int, int, int>)Dig;
            script.Globals["Mine"] = (Action<int, int, int>)Mine;
            script.Globals["Build"] = (Action<int, int, int, string>)Build;
            script.Globals["Harvest"] = (Action<int, int, int>)Harvest;
            script.Globals["IsOre"] = (Func<int, int, int, bool>)IsOre;
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
