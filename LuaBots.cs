using System;
using System.IO;
using UnityEngine;
using Lidgren.Network;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;

public class LuaBots : FortressCraftMod
{
    private float guiTimer;
    private float saveTimer;
    private bool guiClosed;
    private bool foundPlayer;
    private int luaBotItemID;

    private LuaBot currentRobot;
    private Vector2 scrollPosition;

    private Texture2D consoleTexture;
    private LocalPlayerScript localPlayer;

    private Coroutine audioLoadingCoroutine;
    private Coroutine serverUpdateCoroutine;
    private Coroutine networkMoveCoroutine;
    private static Coroutine saveCoroutine;
    private static Coroutine botInfoCoroutine;

    public bool displayGUI;
    private bool robotsLoaded;
    private bool botInfoUpdate;
    private bool digButtonPressed;
    private bool buildButtonPressed;
    private static bool updatingBotInfo;

    private static bool clientUpdate;
    private static int clientUpdateID;
    private static string clientUpdateOutput;
    private static string clientUpdateInventory;
    private static Vector3 clientUpdatePos;
    private static Vector3 clientUpdateLook;
    private static string clientUpdateSound;
    private static bool removeClientBot;

    private static bool serverUpdate;
    private static int serverUpdateID;
    private static Vector3 serverUpdatePos;
    private static string serverUpdateFileName;
    private static string serverUpdateScript;
    private static bool removeServerBot;

    private static bool spawningRobot;
    private static bool destroyingRobot;
    private static Vector3 robotSpawnPos;
    private static int robotSpawnID;
    private static ParticleSystem spawnEffect;

    private static Mesh robotMesh;
    private static Texture2D robotTexture;
    private Texture2D guiBackgroundTexture;

    private static AudioClip digSound;
    private static AudioClip bootSound;
    private static AudioClip buildSound;
    private static AudioClip robotSound;
    private Dictionary<string, AudioClip> audioDictionary;

    private static readonly string assemblyFolder = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
    private static readonly string guiBackgroundTextureString = Path.Combine(assemblyFolder, "Images/background.png");
    private static readonly string robotTexturePath = Path.Combine(assemblyFolder, "Images/robot.png");
    private static readonly string robotModelPath = Path.Combine(assemblyFolder, "Models/robot.obj");
    private static readonly string digAudioPath = Path.Combine(assemblyFolder, "Sounds/dig.wav");
    private static readonly string bootAudioPath = Path.Combine(assemblyFolder, "Sounds/boot.wav");
    private static readonly string buildAudioPath = Path.Combine(assemblyFolder, "Sounds/build.wav");
    private static readonly string robotAudioPath = Path.Combine(assemblyFolder, "Sounds/robot.wav");
    private UriBuilder guiTexUriBuildier = new UriBuilder(guiBackgroundTextureString);
    private UriBuilder robotTextureUriBuilder = new UriBuilder(robotTexturePath);
    private UriBuilder digAudioUribuilder = new UriBuilder(digAudioPath);
    private UriBuilder bootAudioUribuilder = new UriBuilder(bootAudioPath);
    private UriBuilder buildAudioUribuilder = new UriBuilder(buildAudioPath);
    private UriBuilder robotAudioUribuilder = new UriBuilder(robotAudioPath);

    // Holds information for network messages.
    private struct LuaBotNetworkMessage
    {
        public int id;
        public int msgType;
        public Vector3 position;
        public Vector3 lookDir;
        public string fileName;
        public string program;
        public string output;
        public string inventory;
        public string sound;
    }

    // Mod registration.
    public override ModRegistrationData Register()
    {
        ModRegistrationData modRegistrationData = new ModRegistrationData();
        modRegistrationData.RegisterServerComms("Maverick.LuaBots", ServerWrite, ClientRead);
        modRegistrationData.RegisterClientComms("Maverick.LuaBots", ClientWrite, ServerRead);
        return modRegistrationData;
    }

    // Initialization
    public IEnumerator Start()
    {
        MaterialData.GetItemIdOrCubeValues("Maverick.LuaBot", out luaBotItemID, out ushort cubeType, out ushort cubeValue);
        robotTextureUriBuilder.Scheme = "file";
        robotTexture = new Texture2D(2048, 2048, TextureFormat.DXT5, false);
        using (WWW www = new WWW(robotTextureUriBuilder.ToString()))
        {
            yield return www;
            www.LoadImageIntoTexture(robotTexture);
        }

        guiTexUriBuildier.Scheme = "file";
        guiBackgroundTexture = new Texture2D(598, 358, TextureFormat.DXT5, false);
        using (WWW www = new WWW(guiTexUriBuildier.ToString()))
        {
            yield return www;
            www.LoadImageIntoTexture(guiBackgroundTexture);
        }

        consoleTexture = new Texture2D(1, 1);
        consoleTexture.SetPixel(0, 0, new Color(0.0f, 0.0f, 0.0f, 0.5f));
        consoleTexture.Apply();

        ObjImporter importer = new ObjImporter();
        robotMesh = importer.ImportFile(robotModelPath);

        audioDictionary = new Dictionary<string, AudioClip>();
        audioLoadingCoroutine = StartCoroutine(LoadAudio());
    }

    // Loads audio files from disk.
    private IEnumerator LoadAudio()
    {
        digAudioUribuilder.Scheme = "file";
        using (WWW www = new WWW(digAudioUribuilder.ToString()))
        {
            yield return www;
            digSound = www.GetAudioClip();
        }

        bootAudioUribuilder.Scheme = "file";
        using (WWW www = new WWW(bootAudioUribuilder.ToString()))
        {
            yield return www;
            bootSound = www.GetAudioClip();
        }

        buildAudioUribuilder.Scheme = "file";
        using (WWW www = new WWW(buildAudioUribuilder.ToString()))
        {
            yield return www;
            buildSound = www.GetAudioClip();
        }

        robotAudioUribuilder.Scheme = "file";
        using (WWW www = new WWW(robotAudioUribuilder.ToString()))
        {
            yield return www;
            robotSound = www.GetAudioClip();
        }

        audioDictionary.Add("dig", digSound);
        audioDictionary.Add("boot", bootSound);
        audioDictionary.Add("build", buildSound);
    }

    // Sends robot positions from server to clients.
    private IEnumerator DistributeBotInfo()
    {
        botInfoUpdate = true;
        LuaBot[] robots = FindObjectsOfType<LuaBot>();
        for (int i = 0; i < robots.Length; i++)
        {
            if (robots[i] != null && destroyingRobot == false)
            {
                LuaBotNetworkMessage message = new LuaBotNetworkMessage
                {
                    msgType = 1,
                    id = robots[i].id,
                    position = robots[i].transform.position,
                    lookDir = robots[i].lookDir,
                    fileName = robots[i].fileName,
                    program = robots[i].program,
                    output = robots[i].outputDisplay,
                    inventory = robots[i].inventoryDisplay,
                    sound = robots[i].networkSound
                };
                botInfoCoroutine = StartCoroutine(SendBotInfoToClients(message));
                robots[i].networkSound = "";
            }
            yield return new WaitForSeconds(0.125f);
        }
        botInfoUpdate = false;
    }

    // Handles incoming network messages from clients.
    private void UpdateServer()
    {
        LuaBot[] robots = FindObjectsOfType<LuaBot>();
        for (int i = 0; i < robots.Length; i++)
        {
            if (robots[i].id == serverUpdateID)
            {
                robots[i].fileName = serverUpdateFileName;
                robots[i].program = serverUpdateScript;
                robots[i].RunScript();

                if (removeServerBot)
                {
                    string worldName = WorldScript.instance.mWorldData.mName;
                    string robotFilePath = Path.Combine(assemblyFolder, "Save/" + worldName + "/" + robots[i].id + ".sav");
                    File.Delete(robotFilePath);
                    Destroy(robots[i].gameObject);
                }

                break;
            }
        }

        if (removeServerBot)
        {
            LuaBotNetworkMessage message = new LuaBotNetworkMessage
            {
                msgType = 2,
                id = serverUpdateID,
                position = serverUpdatePos,
                lookDir = new Vector3(serverUpdatePos.x, serverUpdatePos.y, serverUpdatePos.z + 1),
                fileName = "",
                program = "",
                output = "",
                inventory = "",
                sound = ""
            };
            botInfoCoroutine = StartCoroutine(SendBotInfoToClients(message));
            removeServerBot = false;
            destroyingRobot = true;
        }

        serverUpdate = false;
    }

    // Handles incoming network messages from the server.
    private void UpdateClient()
    {
        bool foundRobot = false;
        LuaBot[] robots = FindObjectsOfType<LuaBot>();
        for (int i = 0; i < robots.Length; i++)
        {
            if (robots[i].id == clientUpdateID)
            {
                foundRobot = true;
                Vector3 moveDir = (clientUpdatePos - robots[i].transform.position).normalized;
                Vector3 lookDir = new Vector3(clientUpdateLook.x, robots[i].transform.position.y, clientUpdateLook.z);
                robots[i].transform.LookAt(lookDir);
                robots[i].outputDisplay = clientUpdateOutput;
                robots[i].inventoryDisplay = clientUpdateInventory;
                networkMoveCoroutine = StartCoroutine(MoveNetworkBot(robots[i].gameObject, moveDir, clientUpdatePos));

                if (clientUpdateSound != "" && audioDictionary.ContainsKey(clientUpdateSound))
                {
                    AudioSource.PlayClipAtPoint(audioDictionary[clientUpdateSound], robots[i].transform.position);
                }

                if (removeClientBot)
                {
                    SpawnEffects(robots[i].transform.position);
                    Destroy(robots[i].gameObject);
                }

                break;
            }
        }
        if (foundRobot == false && removeClientBot == false)
        {
            spawningRobot = true;
            robotSpawnPos = clientUpdatePos;
            robotSpawnID = clientUpdateID;
        }
        removeClientBot = false;
        clientUpdate = false;
    }

    // Moves networked robot to the position received by the server.
    private IEnumerator MoveNetworkBot(GameObject robot, Vector3 dir, Vector3 target)
    {
        for (int i = 0; i < 4; i++)
        {
            if (robot != null)
            {
                robot.transform.position += new Vector3(dir.x * 0.25f, dir.y * 0.25f, dir.z * 0.25f);
            }
            yield return new WaitForSeconds(0.01f);
        }

        if (robot != null)
        {
            robot.transform.position = target;
        }
    }

    // Called once per frame by unity engine.
    public void Update()
    {
        if (WorldScript.mbIsServer && WorldScript.instance != null)
        {
            if (WorldScript.instance.mWorldData != null)
            {
                if (!robotsLoaded)
                {
                    LoadRobots();
                }
                else if (GameState.State == GameStateEnum.Playing)
                {
                    saveTimer += 1 * Time.deltaTime;
                    if (saveTimer >= 30)
                    {
                        saveCoroutine = StartCoroutine(SaveRobots());
                        saveTimer = 0;
                    }
                }
            }
        }

        if (spawningRobot == true)
        {
            SpawnRobot(robotSpawnPos, robotSpawnID, this);
            spawningRobot = false;
        }

        if (serverUpdate)
        {
            UpdateServer();
        }

        if (clientUpdate == true)
        {
            UpdateClient();
        }

        if (NetworkManager.instance != null && !botInfoUpdate)
        {
            if (NetworkManager.instance.mServerThread != null)
            {
                serverUpdateCoroutine = StartCoroutine(DistributeBotInfo());
            }
        }

        if (Input.GetKeyDown(KeyCode.E))
        {
            if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 50))
            {
                LuaBot robot = hit.collider.gameObject.GetComponent<LuaBot>();
                if (robot != null && !displayGUI)
                {
                    currentRobot = robot;
                    OpenGUI();
                }
            }
        }

        bool b2Pressed = (Input.GetButton("Fire2") || Input.GetAxis("Fire2") > 0.5f);
        if (b2Pressed && !UIManager.CursorShown && !digButtonPressed && !guiClosed)
        { 
            if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.forward, out RaycastHit hit, 50))
            {
                LuaBot robot = hit.collider.gameObject.GetComponent<LuaBot>();
                if (robot != null && !displayGUI)
                {
                    currentRobot = robot;
                    DestroyRobot();
                }
            }
        }
        digButtonPressed = b2Pressed;

        if (!foundPlayer)
        {
            localPlayer = FindObjectOfType<LocalPlayerScript>();
            foundPlayer |= localPlayer != null;
        }

        if (localPlayer != null && !guiClosed)
        {
            bool b1Pressed = (Input.GetButton("Fire1") || Input.GetAxis("Fire1") > 0.5f);
            if (b1Pressed && !buildButtonPressed && !UIManager.CursorShown)
            { 
                int tab = SurvivalHotBarManager.CurrentTab;
                int block = SurvivalHotBarManager.CurrentBlock;
                var selectedItem = SurvivalHotBarManager.instance.maEntries[tab, block];
                ItemEntry itemEntry = ItemEntry.mEntries[selectedItem.itemType];
                if (itemEntry.Name == "Lua Bot")
                {
                    if (CraftRobot())
                    {
                        RequestNewRobot();
                    }
                }
            }
            buildButtonPressed = b1Pressed;
        }

        if (displayGUI)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseGUI();
            }
        }

        if (guiClosed)
        { 
            guiTimer += Time.deltaTime;
            if (guiTimer >= 0.5f)
            {
                guiTimer = 0.0f;
                guiClosed = false;
            }
        }
    }

    // Prepares client to server network messages.
    private static void ClientWrite(BinaryWriter writer, object data)
    {
        LuaBotNetworkMessage message = (LuaBotNetworkMessage)data;
        writer.Write(message.msgType);
        writer.Write(message.position.x);
        writer.Write(message.position.y);
        writer.Write(message.position.z);
        writer.Write(message.fileName);
        writer.Write(message.program);
        writer.Write(message.id);
    }

    // Prepares server to client network messages.
    private static void ServerWrite(BinaryWriter writer, Player player, object data)
    {
        LuaBotNetworkMessage message = (LuaBotNetworkMessage) data;
        writer.Write(message.id);
        writer.Write(message.msgType);
        writer.Write(message.position.x);
        writer.Write(message.position.y);
        writer.Write(message.position.z);
        writer.Write(message.lookDir.x);
        writer.Write(message.lookDir.y);
        writer.Write(message.lookDir.z);
        writer.Write(message.program);
        writer.Write(message.output);
        writer.Write(message.inventory);
        writer.Write(message.sound);
    }

    // Receives server to client network messages.
    private static void ClientRead(NetIncomingMessage message)
    {
        int id = message.ReadInt32();
        int msgType = message.ReadInt32();
        float posX = message.ReadFloat();
        float posY = message.ReadFloat();
        float posZ = message.ReadFloat();
        float lookX = message.ReadFloat();
        float lookY = message.ReadFloat();
        float lookZ = message.ReadFloat();
        string program = message.ReadString();
        string output = message.ReadString();
        string inventory = message.ReadString();
        string sound = message.ReadString();

        Vector3 robotPos = new Vector3(posX, posY, posZ);
        Vector3 robotLook = new Vector3(lookX, lookY, lookZ);

        if (msgType == 0)
        {
            spawningRobot = true;
            robotSpawnPos = robotPos;
            robotSpawnID = id;
        }

        if (msgType ==  1)
        {
            clientUpdateID = id;
            clientUpdatePos = robotPos;
            clientUpdateLook = robotLook;
            clientUpdateOutput = output;
            clientUpdateInventory = inventory;
            clientUpdateSound = sound;
            clientUpdate = true;
        }

        if (msgType == 2)
        {
            clientUpdateID = id;
            clientUpdatePos = robotPos;
            clientUpdateLook = robotLook;
            clientUpdateOutput = output;
            clientUpdateInventory = inventory;
            removeClientBot = true;
            clientUpdate = true;
        }
    }

    // Receives client to server network messages.
    private static void ServerRead(NetIncomingMessage message, Player player)
    {
        int msgType = message.ReadInt32();
        float x = message.ReadFloat();
        float y = message.ReadFloat();
        float z = message.ReadFloat();
        string fileName = message.ReadString();
        string program = message.ReadString();
        int id = message.ReadInt32();

        Vector3 position = new Vector3(x, y, z);

        if (msgType == 0)
        {
            spawningRobot = true;
            robotSpawnPos = position;
            robotSpawnID = 0;
        }

        if (msgType == 1)
        {
            serverUpdateID = id;
            serverUpdateFileName = fileName;
            serverUpdateScript = program;
            serverUpdatePos = position;
            serverUpdate = true;
        }

        if (msgType == 2)
        {
            serverUpdateID = id;
            serverUpdateFileName = fileName;
            serverUpdateScript = program;
            serverUpdatePos = position;
            removeServerBot = true;
            serverUpdate = true;
        }
    }

    // Finds the next available id to assign to a robot.
    private static int FindID()
    {
        int id = 0;
        LuaBot[] robots = FindObjectsOfType<LuaBot>();
        foreach (LuaBot robot in robots)
        {
            if (id <= robot.id)
            {
                id = robot.id + 1;
            }
        }
        return id;
    }

    // Sends information about a robot to all connected clients.
    private static IEnumerator SendBotInfoToClients(LuaBotNetworkMessage message)
    {
        while (updatingBotInfo)
        {
            yield return null;
        }

        updatingBotInfo = true;
        if (NetworkManager.instance != null)
        {
            if (NetworkManager.instance.mServerThread != null)
            {
                List<NetworkServerConnection> connections = NetworkManager.instance.mServerThread.connections;
                for (int i = 0; i < connections.Count; i++)
                {
                    if (connections[i] != null)
                    {
                        if (connections[i].mState == eNetworkConnectionState.Playing)
                        {
                            Player player = connections[i].mPlayer;
                            if (player != null)
                            {
                                ModManager.ModSendServerCommToClient("Maverick.LuaBots", player, message);
                                yield return new WaitForSeconds(0.025f);
                            }
                        }
                    }
                }
            }
        }
        updatingBotInfo = false;
        destroyingRobot = false;
    }

    // Creates a new robot.
    private static GameObject SpawnRobot(Vector3 pos, int id, MonoBehaviour starter)
    {
        if (NetworkManager.instance != null)
        {
            if (NetworkManager.instance.mServerThread != null)
            {
                id = FindID();
                LuaBotNetworkMessage message = new LuaBotNetworkMessage
                {
                    msgType = 0,
                    id = id,
                    position = pos,
                    lookDir = new Vector3(pos.x,pos.y,pos.z + 1),
                    fileName = "",
                    program = "",
                    output = "",
                    inventory = "",
                    sound = ""
                };
                botInfoCoroutine = starter.StartCoroutine(SendBotInfoToClients(message));
            }
        }

        GameObject robot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        robot.GetComponent<MeshFilter>().mesh = robotMesh;
        robot.GetComponent<Renderer>().material.mainTexture = robotTexture;
        robot.AddComponent<AudioSource>();
        robot.AddComponent<LuaBot>();
        robot.GetComponent<LuaBot>().id = id;
        robot.GetComponent<LuaBot>().startPosition = pos;
        robot.GetComponent<LuaBot>().lookDir = new Vector3(pos.x, pos.y, pos.z + 1);
        robot.GetComponent<LuaBot>().inventory = new List<ItemBase>();
        robot.GetComponent<LuaBot>().digSound = digSound;
        robot.GetComponent<LuaBot>().buildSound = buildSound;
        robot.GetComponent<LuaBot>().robotSound = robotSound;
        robot.transform.position = pos;
        SpawnEffects(pos);
        return robot;
    }

    // Plays boot sound and spawns particle effects.
    private static void SpawnEffects(Vector3 pos)
    {
        if (bootSound != null)
        {
            AudioSource.PlayClipAtPoint(bootSound, pos);
        }

        if (spawnEffect == null)
        {
            if (SurvivalParticleManager.instance != null)
            {
                if (SurvivalParticleManager.instance.NetworkBuildParticles != null)
                {
                    spawnEffect = SurvivalParticleManager.instance.NetworkBuildParticles;
                }
            }
        }
        else
        {
            spawnEffect.transform.position = pos;
            spawnEffect.Emit(15);
        }
    }

    // Opens the robot GUI.
    private void OpenGUI()
    {
        UIManager.AllowBuilding = false;
        UIManager.AllowInteracting = false;
        UIManager.AllowMovement = false;
        UIManager.CrossHairShown = false;
        UIManager.CursorShown = true;
        UIManager.GamePaused = true;
        UIManager.HotBarShown = false;
        UIManager.HudShown = false;
        Cursor.visible = true;
        Cursor.lockState = CursorLockMode.None;
        currentRobot.UpdateInventory();
        displayGUI = true;
    }

    // Closes the robot GUI.
    private void CloseGUI()
    {
        UIManager.AllowBuilding = true;
        UIManager.AllowInteracting = true;
        UIManager.AllowMovement = true;
        UIManager.CrossHairShown = true;
        UIManager.CursorShown = false;
        UIManager.GamePaused = false;
        UIManager.HotBarShown = true;
        UIManager.HudShown = true;
        Cursor.visible = false;
        Cursor.lockState = CursorLockMode.Locked;
        displayGUI = false;
        guiClosed = true;
    }

    // Saves a lua script to disk.
    private void SaveProgram()
    {
        string scriptFolder = Path.Combine(assemblyFolder, "Scripts");
        Directory.CreateDirectory(scriptFolder);
        string filePath = Path.Combine(scriptFolder, currentRobot.fileName);
        File.WriteAllText(filePath, currentRobot.program);
    }

    // Loads a lua script from disk.
    private bool LoadProgram()
    {
        string scriptFolder = Path.Combine(assemblyFolder, "Scripts");
        Directory.CreateDirectory(scriptFolder);
        string filePath = Path.Combine(scriptFolder, currentRobot.fileName);
        if (File.Exists(filePath))
        {
            currentRobot.program = File.ReadAllText(filePath);
            return true;
        }
        return false;
    }

    // Saves information about all robots in the world to disk.
    private static IEnumerator SaveRobots()
    {
        string worldName = WorldScript.instance.mWorldData.mName;
        string saveFolder = Path.Combine(assemblyFolder, "Save/" + worldName);
        Directory.CreateDirectory(saveFolder);
        LuaBot[] robots = FindObjectsOfType<LuaBot>();
        for (int i = 0; i < robots.Length; i++)
        {
            string filePath = Path.Combine(saveFolder, robots[i].id.ToString() + ".sav");
            string fileContent = robots[i].startPosition.x + ","
            + robots[i].startPosition.y + "," 
            + robots[i].startPosition.z + ":"
            + robots[i].fileName + "}\n";
            if (robots[i].inventory != null)
            {
                for (int j = 0; j < robots[i].inventory.Count; j++)
                {
                    if (robots[i].inventory[j].mType == ItemType.ItemCubeStack)
                    {
                        ItemCubeStack stack = (ItemCubeStack) robots[i].inventory[j];
                        fileContent += "ItemCubeStack" + ":"
                        + stack.mCubeType + ","
                        + stack.mCubeValue + ","
                        + stack.mnAmount + "\n";
                    }
                    else if (robots[i].inventory[j].mType == ItemType.ItemStack)
                    {
                        ItemStack stack = (ItemStack) robots[i].inventory[j];
                        fileContent += "ItemStack" + ":"
                        + stack.mnItemID + ","
                        + stack.mnAmount + "\n";
                    }
                    else if (robots[i].inventory[j].mType == ItemType.ItemSingle)
                    {
                        ItemSingle single = (ItemSingle)robots[i].inventory[j];
                        fileContent += "ItemSingle" + ":"
                        + single.mnItemID + "\n";
                    }
                    yield return null;
                }
            }
            File.WriteAllText(filePath, fileContent);
            yield return null;
        }
    }

    // Loads all robots associated with the world on startup.
    private void LoadRobots()
    {
        if (WorldScript.instance.mPlayerFrustrum != null)
        {
            string worldName = WorldScript.instance.mWorldData.mName;
            string saveFolder = Path.Combine(assemblyFolder, "Save/" + worldName);
            Directory.CreateDirectory(saveFolder);
            DirectoryInfo dinfo = new DirectoryInfo(saveFolder);
            foreach (FileInfo file in dinfo.GetFiles("*.sav"))
            {
                string filePath = Path.Combine(saveFolder, file.Name);
                string fileContents = File.ReadAllText(filePath);
                int robotID = int.Parse(file.Name.Remove(file.Name.Length - 4));
                string botInfo = fileContents.Split('}')[0];
                string posStr = botInfo.Split(':')[0];
                float x = float.Parse(posStr.Split(',')[0]);
                float y = float.Parse(posStr.Split(',')[1]);
                float z = float.Parse(posStr.Split(',')[2]);

                Vector3 spawnPos = new Vector3(x, y, z);
                GameObject robot = SpawnRobot(spawnPos, robotID, this);

                try
                {
                    if (robot != null)
                    {
                        currentRobot = robot.GetComponent<LuaBot>();
                    }

                    if (currentRobot != null)
                    {
                        string botInventory = fileContents.Split('}')[1];
                        string[] inventoryItems = botInventory.Split('\n');
                        foreach (string itemString in inventoryItems)
                        {
                            string[] itemEntry = itemString.Split(':');
                            if (itemEntry.Length > 1)
                            {
                                if (itemEntry[0] == "ItemCubeStack")
                                {
                                    ushort cubeType = ushort.Parse(itemEntry[1].Split(',')[0]);
                                    ushort cubeValue = ushort.Parse(itemEntry[1].Split(',')[1]);
                                    int cubeAmount = int.Parse(itemEntry[1].Split(',')[2]);
                                    ItemCubeStack stack = new ItemCubeStack(cubeType, cubeValue, cubeAmount);
                                    currentRobot.inventory.Add(stack);
                                }
                                else if (itemEntry[0] == "ItemStack")
                                {
                                    int itemID = int.Parse(itemEntry[1].Split(',')[0]);
                                    int itemAmount = int.Parse(itemEntry[1].Split(',')[1]);
                                    ItemStack stack = new ItemStack(itemID, itemAmount);
                                    currentRobot.inventory.Add(stack);
                                }
                                else if (itemEntry[0] == "ItemSingle")
                                {
                                    int itemID = int.Parse(itemEntry[1]);
                                    ItemSingle item = new ItemSingle(itemID);
                                    currentRobot.inventory.Add(item);
                                }
                            }
                        }

                        currentRobot.UpdateInventory();
                        currentRobot.fileName = botInfo.Split(':')[1];

                        if (LoadProgram())
                        {
                            currentRobot.RunScript();
                        }
                    }
                }
                catch(Exception e)
                {
                    Debug.Log("ERROR LOADING LUA BOTS");
                    Debug.Log("MESSAGE: " + e.Message);
                    Debug.Log("STACK TRACE: " + e.StackTrace);
                }

                WorldScript.instance.mPlayerFrustrum.GetCoordsFromUnity(spawnPos, out long cX, out long cY, out long cZ);
                cX = cX - 4611686017890516992L;
                cY = cY - 4611686017890516992L;
                cZ = cZ - 4611686017890516992L;
                Debug.Log("[Lua Bots] Loaded robot " + robotID + " at " + cX + "," + cY + "," + cZ);
            }
            robotsLoaded = true;
        }
    }

    private void UpdateHotBar()
    {
        for (int i = 0; i < 10; i++)
        {
            int tab = SurvivalHotBarManager.CurrentTab;
            SurvivalHotBarManager.HotBarEntry entry = SurvivalHotBarManager.instance.maEntries[tab, i];

            int count = 0;
            int lastStackCount = 0;

            if (entry.itemType >= 0)
            {
                count = WorldScript.mLocalPlayer.mInventory.GetSuitAndInventoryItemCount(entry.itemType);
                lastStackCount = WorldScript.mLocalPlayer.mInventory.GetItemCount(entry.itemType, true, true);
            }
            
            entry.count = count;
            entry.lastStackCount = lastStackCount;
            entry.UpdateState();
        }
        SurvivalHotBarManager.MarkAsDirty();
    }

    // Crafts a robot.
    private bool CraftRobot()
    {
        ItemBase[,] items = WorldScript.mLocalPlayer.mInventory.maItemInventory;
        foreach (ItemBase item in items)
        {
            if (item != null)
            {
                if (item.GetName().Equals("Lua Bot"))
                {
                    if (item.GetAmount() > 1)
                    {
                        item.DecrementStack(1);
                    }
                    else
                    {
                        WorldScript.mLocalPlayer.mInventory.RemoveSpecificItem(item);
                    }
                    UpdateHotBar();
                    return true;
                }
            }
        }
        return false;
    }

    // Attempts to spawn a new robot.
    private void RequestNewRobot()
    {
        long x = (long) localPlayer.mPlayerBlockPicker.selectBlockPos.x;
        long y = (long) localPlayer.mPlayerBlockPicker.selectBlockPos.y;
        long z = (long) localPlayer.mPlayerBlockPicker.selectBlockPos.z;
        Vector3 spawnPos = new Vector3(x + 0.5f, y + 1.5f, z + 0.5f);
        if (NetworkManager.instance.mClientThread != null)
        {
            LuaBotNetworkMessage msg = new LuaBotNetworkMessage
            {
                msgType = 0,
                position = spawnPos,
                fileName = "",
                program = ""
            };
            ModManager.ModSendClientCommToServer("Maverick.LuaBots", msg);
        }
        else
        {
            SpawnRobot(spawnPos, FindID(), this);
        }
    }

    // Destroys a robot.
    private void DestroyRobot()
    {
        if (NetworkManager.instance.mClientThread != null)
        {
            LuaBotNetworkMessage msg = new LuaBotNetworkMessage
            {
                msgType = 2,
                id = currentRobot.id,
                position = currentRobot.transform.position,
                fileName = "",
                program = "",
            };
            ModManager.ModSendClientCommToServer("Maverick.LuaBots", msg);
        }
        else
        {
            string worldName = WorldScript.instance.mWorldData.mName;
            string robotFilePath = Path.Combine(assemblyFolder, "Save/" + worldName + "/" + currentRobot.id + ".sav");
            File.Delete(robotFilePath);
            SpawnEffects(currentRobot.transform.position);
            Destroy(currentRobot.gameObject);
        }
        bool foundStack = false;
        ItemBase[,] items = WorldScript.mLocalPlayer.mInventory.maItemInventory;
        foreach (ItemBase item in items)
        {
            if (item != null)
            {
                if (item.GetName().Equals("Lua Bot"))
                {
                    item.IncrementStack(1);
                    foundStack = true;
                    UpdateHotBar();
                    break;
                }
            }
        }
        if (!foundStack)
        {
            ItemStack item = new ItemStack(luaBotItemID, 1);
            WorldScript.mLocalPlayer.mInventory.AddItem(item);
        }
        CloseGUI();
    }

    // Starts the lua script for the current robot.
    private void StartRobot()
    {
        if (currentRobot != null)
        {
            if (NetworkManager.instance.mClientThread != null)
            {
                LuaBotNetworkMessage msg = new LuaBotNetworkMessage
                {
                    msgType = 1,
                    id = currentRobot.id,
                    position = currentRobot.transform.position,
                    fileName = currentRobot.fileName,
                    program = currentRobot.program
                };
                ModManager.ModSendClientCommToServer("Maverick.LuaBots", msg);
            }
            else
            {
                currentRobot.RunScript();
            }
        }
    }

    // Called by Unity engine for rendering and handling GUI events.
    public void OnGUI()
    {
        Rect guiBackgroundRect = new Rect(Screen.width * 0.04f, Screen.width * 0.0425f, Screen.width * 0.75f, Screen.width * 0.5f);
        Rect codeEditingRect = new Rect(Screen.width * 0.1f, Screen.width * 0.1f, Screen.width * 0.3f, Screen.width * 0.3f);
        Rect inventoryRect = new Rect(Screen.width * 0.425f, Screen.width * 0.1f, Screen.width * 0.3f, Screen.width * 0.3f);
        Rect fileNameRect = new Rect(Screen.width * 0.1f, Screen.width * 0.415f, Screen.width * 0.3f, Screen.width * 0.02f);
        Rect runButtonRect = new Rect(Screen.width * 0.12f, Screen.width * 0.445f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect saveButtonRect = new Rect(Screen.width * 0.22f, Screen.width * 0.445f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect loadButtonRect = new Rect(Screen.width * 0.32f, Screen.width * 0.445f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect outputButtonRect = new Rect(Screen.width * 0.425f, Screen.width * 0.415f, Screen.width * 0.12f, Screen.width * 0.02f);
        Rect inventoryButtonRect = new Rect(Screen.width * 0.6f, Screen.width * 0.415f, Screen.width * 0.12f, Screen.width * 0.02f);
        Rect destroyButtonRect = new Rect(Screen.width * 0.425f, Screen.width * 0.465f, Screen.width * 0.12f, Screen.width * 0.02f);
        Rect closeButtonRect = new Rect(Screen.width * 0.6f, Screen.width * 0.465f, Screen.width * 0.12f, Screen.width * 0.02f);

        if (displayGUI)
        {
            GUI.DrawTexture(guiBackgroundRect, guiBackgroundTexture);
            currentRobot.program = GUI.TextArea(codeEditingRect, currentRobot.program);
            currentRobot.fileName = GUI.TextField(fileNameRect, currentRobot.fileName);
            GUI.DrawTexture(inventoryRect, consoleTexture);
            float consoleRectSize = Screen.width * 0.3f;

            if (currentRobot.display == "inventory")
            {
                string[] invArray = currentRobot.inventoryDisplay.Split('\n');
                float textHeight = invArray.Length * GUI.skin.label.lineHeight;
                if (!inventoryRect.Contains(Event.current.mousePosition))
                {
                    scrollPosition = new Vector2(0, consoleRectSize + textHeight);
                }
                Rect consoleRect = new Rect(10, 20, consoleRectSize - 20, textHeight);
                scrollPosition = GUI.BeginScrollView(inventoryRect, scrollPosition, consoleRect, false, false );
                GUI.Label(consoleRect, currentRobot.inventoryDisplay);
                GUI.EndScrollView();
            }
            else
            {
                string[] outputArray = currentRobot.outputDisplay.Split('\n');
                float textHeight = outputArray.Length * GUI.skin.label.lineHeight;
                if (!inventoryRect.Contains(Event.current.mousePosition))
                {
                    scrollPosition = new Vector2(0, consoleRectSize + textHeight);
                }
                Rect consoleRect = new Rect(10, 20, consoleRectSize - 20, textHeight);
                scrollPosition = GUI.BeginScrollView(inventoryRect, scrollPosition, consoleRect, false, false );
                GUI.Label(consoleRect, currentRobot.outputDisplay);
                GUI.EndScrollView();
            }

            if (GUI.Button(runButtonRect, "Run"))
            {
                StartRobot();
            }

            if (GUI.Button(saveButtonRect, "Save"))
            {
                SaveProgram();
            }

            if (GUI.Button(loadButtonRect, "Load"))
            {
                LoadProgram();
            }

            if (GUI.Button(outputButtonRect, "Output"))
            {
                currentRobot.display = "output";
            }

            if (GUI.Button(inventoryButtonRect, "Inventory"))
            {
                currentRobot.display = "inventory";
            }

            if (GUI.Button(closeButtonRect, "Close Window"))
            {
                CloseGUI();
            }

            if (GUI.Button(destroyButtonRect, "Collect Robot"))
            {
                DestroyRobot();
            }
        }
    }
}
