using UnityEngine;
using System;
using System.IO;
using System.Reflection;
using System.Collections;
using Lidgren.Network;
using System.Collections.Generic;

public class LuaBots : FortressCraftMod
{
    private float saveTimer;
    private Robot currentRobot;

    private Coroutine audioLoadingCoroutine;
    private Coroutine serverUpdateCoroutine;
    private Coroutine networkMoveCoroutine;
    private static Coroutine botInfoCoroutine;

    public bool displayGUI;
    private bool botInfoUpdate;
    private bool robotsLoaded;
    private static bool updatingBotInfo;

    private static bool clientUpdate;
    private static int clientUpdateID;
    private static string clientUpdateInventory;
    private static Vector3 clientUpdatePos;
    private static string clientUpdateSound;
    private static bool removeClientBot;

    private static bool serverUpdate;
    private static int serverUpdateID;
    private static Vector3 serverUpdatePos;
    private static string serverUpdateScript;
    private static bool removeServerBot;

    private static bool spawningRobot;
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
        public int msgType;
        public Vector3 position;
        public string program;
        public int id;
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
        Robot[] robots = FindObjectsOfType<Robot>();
        for (int i = 0; i < robots.Length; i++)
        {
            if (!updatingBotInfo)
            {
                LuaBotNetworkMessage message = new LuaBotNetworkMessage
                {
                    msgType = 1,
                    id = robots[i].id,
                    position = robots[i].transform.position,
                    program = robots[i].program,
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
        Robot[] robots = FindObjectsOfType<Robot>();
        for (int i = 0; i < robots.Length; i++)
        {
            if (robots[i].id == serverUpdateID)
            {
                robots[i].program = serverUpdateScript;
                robots[i].RunScript();

                if (removeServerBot)
                {
                    removeServerBot = false;
                    string worldName = WorldScript.instance.mWorldData.mName;
                    string robotFilePath = Path.Combine(assemblyFolder, "Save/" + worldName + "/" + robots[i].id + ".sav");
                    File.Delete(robotFilePath);
                    Destroy(robots[i].gameObject);

                    if (!updatingBotInfo)
                    {
                        LuaBotNetworkMessage message = new LuaBotNetworkMessage
                        {
                            msgType = 2,
                            id = serverUpdateID,
                            position = serverUpdatePos,
                            program = "",
                            inventory = "",
                            sound = ""
                        };
                        botInfoCoroutine = StartCoroutine(SendBotInfoToClients(message));
                    }
                }

                break;
            }
        }
        serverUpdate = false;
    }

    // Handles incoming network messages from the server.
    private void UpdateClient()
    {
        bool foundRobot = false;
        Robot[] robots = FindObjectsOfType<Robot>();
        for (int i = 0; i < robots.Length; i++)
        {
            if (robots[i].id == clientUpdateID)
            {
                foundRobot = true;
                Vector3 moveDir = (clientUpdatePos - robots[i].transform.position).normalized;
                networkMoveCoroutine = StartCoroutine(MoveNetworkBot(robots[i].gameObject, moveDir, clientUpdatePos));
                robots[i].inventoryDisplay = clientUpdateInventory;

                if (clientUpdateSound != "" && audioDictionary.ContainsKey(clientUpdateSound))
                {
                    AudioSource.PlayClipAtPoint(audioDictionary[clientUpdateSound], robots[i].transform.position);
                }

                if (removeClientBot)
                {
                    SpawnEffects(robots[i].transform.position);
                    Destroy(robots[i].gameObject);
                    removeClientBot = false;
                }

                break;
            }
        }
        if (foundRobot == false)
        {
            spawningRobot = true;
            robotSpawnPos = clientUpdatePos;
            robotSpawnID = clientUpdateID;
        }
        clientUpdate = false;
    }

    // Moves networked robot to the position received by the server.
    private IEnumerator MoveNetworkBot(GameObject robot, Vector3 dir, Vector3 target)
    {
        for (int i = 0; i < 4; i++)
        {
            robot.transform.position += new Vector3(dir.x * 0.25f, dir.y * 0.25f, dir.z * 0.25f);
            yield return new WaitForSeconds(0.01f);
        }
        robot.transform.position = target;
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
                else
                {
                    saveTimer += 1 * Time.deltaTime;
                    if (saveTimer >= 30)
                    {
                        SaveRobots();
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
                Robot robot = hit.collider.gameObject.GetComponent<Robot>();
                if (robot != null && !displayGUI)
                {
                    currentRobot = robot;
                    OpenGUI();
                }
            }
        }

        if (displayGUI)
        {
            if (Input.GetKeyDown(KeyCode.Escape))
            {
                CloseGUI();
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
        writer.Write(message.program);
        writer.Write(message.id);
    }

    // Prepares server to client network messages.
    private static void ServerWrite(BinaryWriter writer, Player player, object data)
    {
        LuaBotNetworkMessage message = (LuaBotNetworkMessage) data;
        writer.Write(message.msgType);
        writer.Write(message.position.x);
        writer.Write(message.position.y);
        writer.Write(message.position.z);
        writer.Write(message.program);
        writer.Write(message.id);
        writer.Write(message.inventory);
        writer.Write(message.sound);
    }

    // Receives server to client network messages.
    private static void ClientRead(NetIncomingMessage message)
    {
        int msgType = message.ReadInt32();
        float x = message.ReadFloat();
        float y = message.ReadFloat();
        float z = message.ReadFloat();
        string program = message.ReadString();
        int id = message.ReadInt32();
        string inventory = message.ReadString();
        string sound = message.ReadString();

        Vector3 robotPos = new Vector3(x, y, z);

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
            clientUpdateInventory = inventory;
            clientUpdateSound = sound;
            clientUpdate = true;
        }

        if (msgType == 2)
        {
            clientUpdateID = id;
            clientUpdatePos = robotPos;
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
            serverUpdateScript = program;
            serverUpdatePos = position;
            serverUpdate = true;
        }

        if (msgType == 2)
        {
            serverUpdateID = id;
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
        Robot[] robots = FindObjectsOfType<Robot>();
        foreach (Robot robot in robots)
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
    }

    // Creates a new robot.
    private static GameObject SpawnRobot(Vector3 pos, int id, MonoBehaviour starter)
    {
        if (NetworkManager.instance != null)
        {
            if (NetworkManager.instance.mServerThread != null)
            {
                id = FindID();
                if (!updatingBotInfo)
                {
                    LuaBotNetworkMessage message = new LuaBotNetworkMessage
                    {
                        msgType = 0,
                        id = id,
                        position = pos,
                        program = "",
                        inventory = "",
                        sound = ""
                    };
                    botInfoCoroutine = starter.StartCoroutine(SendBotInfoToClients(message));
                }
            }
        }

        GameObject robot = GameObject.CreatePrimitive(PrimitiveType.Cube);
        robot.GetComponent<MeshFilter>().mesh = robotMesh;
        robot.GetComponent<Renderer>().material.mainTexture = robotTexture;
        robot.AddComponent<AudioSource>();
        robot.AddComponent<Robot>();
        robot.GetComponent<Robot>().id = id;
        robot.GetComponent<Robot>().startPosition = pos;
        robot.GetComponent<Robot>().inventory = new List<ItemBase>();
        robot.GetComponent<Robot>().digSound = digSound;
        robot.GetComponent<Robot>().buildSound = buildSound;
        robot.GetComponent<Robot>().robotSound = robotSound;
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
    private static void SaveRobots()
    {
        string worldName = WorldScript.instance.mWorldData.mName;
        string saveFolder = Path.Combine(assemblyFolder, "Save/" + worldName);
        Directory.CreateDirectory(saveFolder);
        Robot[] robots = FindObjectsOfType<Robot>();
        foreach (Robot robot in robots)
        {
            string filePath = Path.Combine(saveFolder, robot.id.ToString() + ".sav");
            string fileContent = robot.startPosition.x + ","
            + robot.startPosition.y + "," 
            + robot.startPosition.z + ":"
            + robot.fileName + "}\n";
            if (robot.inventory != null)
            {
                foreach (ItemBase item in robot.inventory)
                {
                    if (item.mType == ItemType.ItemCubeStack)
                    {
                        ItemCubeStack stack = (ItemCubeStack) item;
                        fileContent += "ItemCubeStack" + ":"
                        + stack.mCubeType + ","
                        + stack.mCubeValue + ","
                        + stack.mnAmount + "\n";
                    }
                    else if (item.mType == ItemType.ItemStack)
                    {
                        ItemStack stack = (ItemStack) item;
                        fileContent += "ItemStack" + ":"
                        + stack.mnItemID + ","
                        + stack.mnAmount + "\n";
                    }
                    else if (item.mType == ItemType.ItemSingle)
                    {
                        ItemSingle single = (ItemSingle)item;
                        fileContent += "ItemSingle" + ":"
                        + single.mnItemID + "\n";
                    }
                }
            }
            File.WriteAllText(filePath, fileContent);
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
                        currentRobot = robot.GetComponent<Robot>();
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

    // Crafts a robot.
    private bool CraftRobot()
    {
        ItemBase[,] items = WorldScript.mLocalPlayer.mInventory.maItemInventory;
        foreach (ItemBase item in items)
        {
            if (item != null)
            {
                if (item.GetName().Equals("ConstructoBot Crate"))
                {
                    if (item.GetAmount() > 1)
                    {
                        item.DecrementStack(1);
                    }
                    else
                    {
                        WorldScript.mLocalPlayer.mInventory.RemoveSpecificItem(item);
                    }
                    InventoryPanelScript.instance.Reshow();
                    return true;
                }
            }
        }
        return false;
    }

    // Attempts to spawn a new robot.
    private void RequestNewRobot()
    {
        long x = WorldScript.mLocalPlayer.mnWorldX;
        long y = WorldScript.mLocalPlayer.mnWorldY;
        long z = WorldScript.mLocalPlayer.mnWorldZ;
        Vector3 spawnCoords = WorldScript.instance.mPlayerFrustrum.GetCoordsToUnity(x, y, z);
        Vector3 spawnPos = new Vector3(spawnCoords.x + 0.5f, spawnCoords.y + 0.5f, spawnCoords.z + 0.5f);
        if (NetworkManager.instance.mClientThread != null)
        {
            LuaBotNetworkMessage msg = new LuaBotNetworkMessage
            {
                msgType = 0,
                position = spawnPos,
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
        WorldScript.mLocalPlayer.mInventory.CollectValue(eCubeTypes.ConstructoBotCrate, 0, 1);
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
        Rect guiBackgroundRect = new Rect(Screen.width * 0.04f, Screen.width * 0.0425f, Screen.width * 0.75f, Screen.width * 0.475f);
        Rect buttonBackgroundRect = new Rect(Screen.width * 0.795f, Screen.width * 0.027f, Screen.width * 0.2f, Screen.width * 0.08f);
        Rect spawnLuaBotRect = new Rect(Screen.width * 0.81f, Screen.width * 0.037f, Screen.width * 0.17f, Screen.width * 0.06f);
        Rect codeEditingRect = new Rect(Screen.width * 0.1f, Screen.width * 0.1f, Screen.width * 0.3f, Screen.width * 0.3f);
        Rect inventoryRect = new Rect(Screen.width * 0.425f, Screen.width * 0.1f, Screen.width * 0.3f, Screen.width * 0.3f);
        Rect fileNameRect = new Rect(Screen.width * 0.1f, Screen.width * 0.415f, Screen.width * 0.3f, Screen.width * 0.02f);
        Rect runButtonRect = new Rect(Screen.width * 0.1f, Screen.width * 0.44f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect saveButtonRect = new Rect(Screen.width * 0.2f, Screen.width * 0.44f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect loadButtonRect = new Rect(Screen.width * 0.3f, Screen.width * 0.44f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect closeButtonRect = new Rect(Screen.width * 0.4f, Screen.width * 0.44f, Screen.width * 0.06f, Screen.width * 0.02f);
        Rect destroyButtonRect = new Rect(Screen.width * 0.6f, Screen.width * 0.44f, Screen.width * 0.12f, Screen.width * 0.02f);

        if (InventoryPanelScript.mbIsActive)
        {
            GUI.DrawTexture(buttonBackgroundRect, guiBackgroundTexture);
            if (GUI.Button(spawnLuaBotRect, "Spawn Lua Bot\n(1x ConstructoBot Crate)"))
            {
                if (CraftRobot())
                {
                    RequestNewRobot();
                }
            }
        }

        if (displayGUI)
        {
            GUI.DrawTexture(guiBackgroundRect, guiBackgroundTexture);
            currentRobot.program = GUI.TextArea(codeEditingRect, currentRobot.program);
            currentRobot.fileName = GUI.TextField(fileNameRect, currentRobot.fileName);
            GUI.TextArea(inventoryRect, currentRobot.inventoryDisplay);

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

            if (GUI.Button(closeButtonRect, "Close"))
            {
                CloseGUI();
            }

            if (GUI.Button(destroyButtonRect, "Destroy Robot"))
            {
                DestroyRobot();
            }
        }
    }
}
