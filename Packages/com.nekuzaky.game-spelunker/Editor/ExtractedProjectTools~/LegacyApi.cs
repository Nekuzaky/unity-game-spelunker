#if SPELUNKER_GAME_CODE
namespace GameSpelunker
{
    public static class Compat
    {
        public static UnityEngine.Material NewMaterial(UnityEngine.Shader shader) { return new UnityEngine.Material(shader); }
        public static UnityEngine.Material NewMaterial(UnityEngine.Material source) { return new UnityEngine.Material(source); }
        public static UnityEngine.Material NewMaterial(string shaderSource) { return new UnityEngine.Material(UnityEngine.Shader.Find("Standard")); }

        public static void DrawProceduralIndirect(UnityEngine.MeshTopology topology, UnityEngine.ComputeBuffer bufferWithArgs, int argsOffset = 0)
        {
            UnityEngine.Graphics.DrawProceduralIndirectNow(topology, bufferWithArgs, argsOffset);
        }

        public static void DrawProceduralIndirect(UnityEngine.Material material, UnityEngine.Bounds bounds, UnityEngine.MeshTopology topology,
            UnityEngine.ComputeBuffer bufferWithArgs, int argsOffset = 0, UnityEngine.Camera camera = null, UnityEngine.MaterialPropertyBlock properties = null,
            UnityEngine.Rendering.ShadowCastingMode castShadows = UnityEngine.Rendering.ShadowCastingMode.On, bool receiveShadows = true, int layer = 0)
        {
            UnityEngine.Graphics.DrawProceduralIndirect(material, bounds, topology, bufferWithArgs, argsOffset, camera, properties, castShadows, receiveShadows, layer);
        }
    }
}

namespace UnityEngine
{
    public struct Particle
    {
        public Vector3 position;
        public Vector3 velocity;
        public float energy;
        public float startEnergy;
        public float size;
        public float rotation;
        public float angularVelocity;
        public Color color;
    }

    public class ParticleEmitter : Behaviour
    {
        public bool emit;
        public float minSize;
        public float maxSize;
        public float minEnergy;
        public float maxEnergy;
        public float minEmission;
        public float maxEmission;
        public float emitterVelocityScale;
        public Vector3 worldVelocity;
        public Vector3 localVelocity;
        public Vector3 rndVelocity;
        public bool useWorldSpace;
        public bool rndRotation;
        public float angularVelocity;
        public float rndAngularVelocity;
        public Particle[] particles = new Particle[0];
        public int particleCount { get { return 0; } }
        public void ClearParticles() { }
        public void Emit() { }
        public void Emit(int count) { }
        public void Emit(Vector3 pos, Vector3 velocity, float size, float energy, Color color) { }
        public void Emit(Vector3 pos, Vector3 velocity, float size, float energy, Color color, float rotation, float angularVelocity) { }
        public void Simulate(float deltaTime) { }
    }

    public class ParticleAnimator : Behaviour
    {
        public bool doesAnimateColor;
        public Vector3 worldRotationAxis;
        public Vector3 localRotationAxis;
        public float sizeGrow;
        public Vector3 rndForce;
        public Vector3 force;
        public float damping;
        public bool autodestruct;
        public Color[] colorAnimation = new Color[5];
    }

    public enum ParticleRenderMode { Billboard = 0, Stretch = 3, SortedBillboard = 2, HorizontalBillboard = 4, VerticalBillboard = 5 }

    public class ParticleRenderer : Renderer
    {
        public ParticleRenderMode particleRenderMode;
        public float lengthScale;
        public float velocityScale;
        public float cameraVelocityScale;
        public float maxParticleSize;
        public int uvAnimationXTile;
        public int uvAnimationYTile;
        public float uvAnimationCycles;
        public float maxPartileSize;
        public Rect[] uvTiles = new Rect[0];
    }

    public sealed class RPC { }

    [System.AttributeUsage(System.AttributeTargets.Method)]
    public sealed class RPCAttribute : System.Attribute { }

    public enum RPCMode { Server = 0, Others = 1, OthersBuffered = 5, All = 2, AllBuffered = 6 }

    public enum NetworkPeerType { Disconnected = 0, Server = 1, Client = 2, Connecting = 3 }

    public static class Network
    {
        public static bool isServer { get { return false; } }
        public static bool isClient { get { return false; } }
        public static bool isMessageQueueRunning { get; set; }
        public static NetworkPeerType peerType { get { return NetworkPeerType.Disconnected; } }
        public static NetworkPlayer player { get { return new NetworkPlayer(); } }
        public static NetworkPlayer[] connections { get { return new NetworkPlayer[0]; } }
        public static double time { get { return Time.timeAsDouble; } }
        public static int sendRate { get; set; }
        public static void Disconnect() { }
        public static void Disconnect(int timeout) { }
        public static void Destroy(Object target) { Object.Destroy(target); }
        public static void Destroy(NetworkViewID viewID) { }
        public static Object Instantiate(Object prefab, Vector3 position, Quaternion rotation, int group) { return Object.Instantiate(prefab, position, rotation); }
        public static void RemoveRPCs(NetworkPlayer playerToRemove) { }
        public static void RemoveRPCs(NetworkViewID viewID) { }
        public static void DestroyPlayerObjects(NetworkPlayer playerToDestroy) { }
        public static void SetSendingEnabled(int group, bool enabled) { }
    }

    public enum NetworkStateSynchronization { Off = 0, ReliableDeltaCompressed = 1, Unreliable = 2 }

    public struct NetworkViewID
    {
        public static NetworkViewID unassigned { get { return new NetworkViewID(); } }
        public bool isMine { get { return true; } }
        public NetworkPlayer owner { get { return new NetworkPlayer(); } }
    }

    public struct NetworkPlayer
    {
        public string ipAddress { get { return ""; } }
        public int port { get { return 0; } }
        public string guid { get { return ""; } }
        public string externalIP { get { return ""; } }
        public int externalPort { get { return 0; } }
    }

    public struct NetworkMessageInfo
    {
        public double timestamp { get { return 0; } }
        public NetworkPlayer sender { get { return new NetworkPlayer(); } }
        public NetworkView networkView { get { return null; } }
    }

    public class NetworkView : Behaviour
    {
        public Component observed;
        public NetworkStateSynchronization stateSynchronization;
        public NetworkViewID viewID;
        public int group;
        public bool isMine { get { return true; } }
        public NetworkPlayer owner { get { return new NetworkPlayer(); } }
        public void RPC(string name, RPCMode mode, params object[] args) { }
        public void RPC(string name, NetworkPlayer target, params object[] args) { }
        public bool SetScope(NetworkPlayer player, bool relevancy) { return false; }
    }
}

namespace UnityEngine.VR
{
    public enum VRNode { LeftEye = 0, RightEye = 1, CenterEye = 2, Head = 3, LeftHand = 4, RightHand = 5, GameController = 6, TrackingReference = 7, HardwareTracker = 8 }

    public enum VRDeviceType { None = 0, Stereo = 1, Split = 2, Oculus = 3, PlayStationVR = 4, Morpheus = 4 }

    public static class VRSettings
    {
        public static bool enabled { get; set; }
        public static bool showDeviceView { get; set; }
        public static float renderScale { get; set; }
        public static float renderViewportScale { get; set; }
        public static int eyeTextureWidth { get { return 0; } }
        public static int eyeTextureHeight { get { return 0; } }
        public static bool isDeviceActive { get { return false; } }
        public static string loadedDeviceName { get { return ""; } }
        public static VRDeviceType loadedDevice { get; set; }
        public static string[] supportedDevices { get { return new string[0]; } }
        public static void LoadDeviceByName(string deviceName) { }
        public static void LoadDeviceByName(string[] prioritizedDeviceNameList) { }
    }

    public static class VRDevice
    {
        public static bool isPresent { get { return false; } }
        public static string model { get { return ""; } }
        public static float refreshRate { get { return 0f; } }
        public static System.IntPtr GetNativePtr() { return System.IntPtr.Zero; }
    }

    public static class VRStats
    {
        public static float gpuTimeLastFrame { get { return 0f; } }
    }

    public static class InputTracking
    {
        public static bool disablePositionalTracking { get; set; }
        public static Vector3 GetLocalPosition(VRNode node) { return Vector3.zero; }
        public static Quaternion GetLocalRotation(VRNode node) { return Quaternion.identity; }
        public static void Recenter() { }
    }
}

namespace GameSpelunker
{
    public class GUIElementStub : UnityEngine.Behaviour
    {
        public bool HitTest(UnityEngine.Vector3 screenPosition) { return false; }
        public bool HitTest(UnityEngine.Vector3 screenPosition, UnityEngine.Camera camera) { return false; }
        public UnityEngine.Rect GetScreenRect() { return new UnityEngine.Rect(); }
        public UnityEngine.Rect GetScreenRect(UnityEngine.Camera camera) { return new UnityEngine.Rect(); }
    }

    public class GUITextStub : GUIElementStub
    {
        public string text = "";
        public UnityEngine.Material material;
        public UnityEngine.Vector2 pixelOffset;
        public UnityEngine.Font font;
        public int fontSize;
        public UnityEngine.FontStyle fontStyle;
        public UnityEngine.TextAlignment alignment;
        public UnityEngine.TextAnchor anchor;
        public float lineSpacing;
        public float tabSize;
        public bool richText;
        public UnityEngine.Color color = UnityEngine.Color.white;
    }

    public class GUITextureStub : GUIElementStub
    {
        public UnityEngine.Color color = UnityEngine.Color.white;
        public UnityEngine.Texture texture;
        public UnityEngine.Rect pixelInset;
        public UnityEngine.RectOffset border = new UnityEngine.RectOffset();
    }

    public class GUILayerStub : UnityEngine.Behaviour
    {
        public GUIElementStub HitTest(UnityEngine.Vector3 screenPosition) { return null; }
    }

    public class MovieTextureStub : UnityEngine.Texture
    {
        public UnityEngine.AudioClip audioClip { get { return null; } }
        public bool loop { get; set; }
        public bool isPlaying { get { return false; } }
        public bool isReadyToPlay { get { return true; } }
        public float duration { get { return 0f; } }
        public void Play() { }
        public void Stop() { }
        public void Pause() { }
    }

    public class ProceduralMaterialStub : UnityEngine.Material
    {
        public ProceduralMaterialStub() : base(UnityEngine.Shader.Find("Standard")) { }
        public bool isProcessing { get { return false; } }
        public bool isReadable { get; set; }
        public bool isLoadTimeGenerated { get; set; }
        public void RebuildTextures() { }
        public void RebuildTexturesImmediately() { }
        public void SetProceduralFloat(string inputName, float value) { }
        public float GetProceduralFloat(string inputName) { return 0f; }
        public void SetProceduralBoolean(string inputName, bool value) { }
        public void SetProceduralColor(string inputName, UnityEngine.Color value) { }
        public void SetProceduralVector(string inputName, UnityEngine.Vector4 value) { }
        public void SetProceduralTexture(string inputName, UnityEngine.Texture2D value) { }
        public void SetProceduralEnum(string inputName, int value) { }
        public UnityEngine.Texture[] GetGeneratedTextures() { return new UnityEngine.Texture[0]; }
        public static void StopRebuilds() { }
    }
}
#endif
