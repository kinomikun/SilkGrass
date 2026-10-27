// Scene-wide uniform blocks, uploaded once per frame by Rendering/UniformBuffers.cs.
// The C# structs there mirror these std140 layouts field for field - change both
// together. A vec3 followed by a float packs into one 16-byte slot.
//
// Bindings: 1 FrameData, 2 WindData, 3 LookData, 4 AtmosphereData,
// 5 SwellData (common_swell.glsl), 6 CullCells (grass_cull.comp).

layout(std140, binding = 1) uniform FrameData {
    mat4 View;
    mat4 Projection;        // jittered while TAA is on (rasterization)
    mat4 TaaCurrViewProj;   // unjittered (motion vectors)
    mat4 TaaPrevViewProj;
    vec3 CameraPosition;  float Time;
    vec3 CenterPosition;  float CloudShadowTime;  // field centre; cloud drift phase
    vec3 SunDirection;    float TaaActive;        // SunDirection is normalized
    float TaaPrevTime;
    float TaaPrevWindTime;
    float _framePad0;
    float _framePad1;
};

layout(std140, binding = 2) uniform WindData {
    vec2 WindDir;           // normalized
    float WindSpeed;
    float WindStrength;
    float WindFrequency;
    float VerticalBend;
    float BaseLeanStrength;
    float WindTime;         // accumulated wind scroll phase (integrated on the CPU)
    vec2 BaseLeanDir;       // normalized
    float BladeHeight;
    float _windPad0;
};

layout(std140, binding = 3) uniform LookData {
    vec3 GrassTop;           float TerrainFrequency;
    vec3 GrassBottom;        float TerrainAmplitude;
    vec3 WindTint;           float ValleyY;
    vec3 ValleyColor;        float HillY;
    vec3 HillColor;          float GradientIntensity;
    vec3 PatchDarkTint;      float PatchNoiseFreq;
    vec3 PatchLightTint;     float PatchDarkThreshold;
    vec3 GroundColor;        float PatchLightThreshold;
    vec3 HorizonTopColor;    float MacroVarStrength;
    vec3 HorizonBottomColor; float MacroVarFreq;
    vec3 MacroVarDarkTint;   float ShadowSaturation;
    vec3 MacroVarLightTint;  float SheenStrength;
    vec3 ShadowAmbientTint;  float SheenScale;
    vec3 SheenColor;         float SheenSpeed;
    vec2 SheenDir;           float SheenMaxDist;  float _lookPad0;
};

layout(std140, binding = 4) uniform AtmosphereData {
    vec3 AtmosphereColor;  float FogStart;        // AtmosphereColor = fog colour
    float FogDensity;
    float FogHeightFalloff;
    float FogMaxOpacity;
    float CloudThreshold;
    float CloudShadowSharpness;
    float RainEnabled;
    float RainIntensity;
    float WetDarkening;
    float SplashFrequency;
    float SplashBrightness;
    float _atmPad0;
    float _atmPad1;
    vec4 ReliefParams;     // x = strength (0 = off), y = slope for full effect, z = fade start (m), w = fade end (m)
    vec2 ReliefSunAz;      // normalized XZ direction toward the sun
    vec2 ReliefSplit;      // x = how much sun-facing slopes brighten, y = how much lee slopes darken
};
