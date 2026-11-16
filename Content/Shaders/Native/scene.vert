#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
layout(location=3) in vec4 aJoints;
layout(location=4) in vec4 aWeights;
layout(location=5) in vec4 aInstance;
layout(location=6) in vec4 aVariation;
uniform mat4 uModel, uViewProjection;
uniform sampler2D uBones;
uniform int uSkin;
uniform int uSurface;
uniform int uInstanced;
uniform float uTime;
uniform vec3 uEye;
uniform float uPose, uPreviousPose, uPoseBlend;
out vec3 vWorld, vNormal, vLocal;
out vec2 vUv;
uniform vec2 uViewport;
noperspective out vec2 vLine;
flat out vec4 vDetail;

mat4 bone(int joint, int pose)
{
    int x = joint * 4;
    return mat4(texelFetch(uBones, ivec2(x, pose), 0),
                texelFetch(uBones, ivec2(x + 1, pose), 0),
                texelFetch(uBones, ivec2(x + 2, pose), 0),
                texelFetch(uBones, ivec2(x + 3, pose), 0));
}
mat4 poseMatrix(float frame)
{
    int last = textureSize(uBones, 0).y - 1;
    int pose = clamp(int(frame), 0, last), base = pose / 64 * 64;
    int next = min(base + (pose + 1 - base) % 64, last);
    mat4 skin = mat4(0.);
    for (int k = 0; k < 4; ++k)
    {
        if (aWeights[k] <= 0.) continue;
        int j = int(aJoints[k]);
        skin += (bone(j, pose) * (1. - fract(frame)) + bone(j, next) * fract(frame)) * aWeights[k];
    }
    return skin;
}
#include "water.glsl"

void main()
{
    vec4 p = vec4(aPosition, 1.);
    vec3 n = aNormal;
    if (uSkin != 0)
    {
        mat4 skin;
        if (uPoseBlend >= .999) skin = poseMatrix(uPose);
        else if (uPoseBlend <= .001) skin = poseMatrix(uPreviousPose);
        else skin = poseMatrix(uPreviousPose) * (1. - uPoseBlend) + poseMatrix(uPose) * uPoseBlend;
        p = skin * p;
        n = mat3(skin) * n;
    }
    vLocal = p.xyz;
    if (uSkin == 0 && (aJoints.w == -1.0 || aJoints.w == -2.0)) vLocal.xy = aJoints.xy;
    vec4 world = uModel * p;
    if (uSurface == 4)
        world.y += shoreWave(world.xz, uTime).x;
    vec4 detail = aJoints;
    if (uInstanced != 0) {
        mat3 rotation = mat3(aVariation.y,0,-aVariation.x, 0,1,0, aVariation.x,0,aVariation.y);
        if (uInstanced == 2)
            rotation *= mat3(aVariation.w,aVariation.z,0, -aVariation.z,aVariation.w,0, 0,0,1);
        world.xyz = aInstance.xyz + rotation*p.xyz*aInstance.w;
        n = rotation*n;
        detail.y += aVariation.z;
        detail.z = fract(detail.z+aVariation.w);
    }
    if (uSurface == 9) {
        // Use exactly the same deformation in the colour and shadow passes.
        float weight = aJoints.x * aJoints.x;
        float sway = sin(uTime*.85 + detail.y + world.y*.3);
        world.x += weight * (.12*sway + .025*sin(uTime*2.1+world.z));
        world.z += weight * .065*cos(uTime*.73+detail.y+world.y*.2);
    }
    if (uSurface == 12) {
        float scale = 1. - smoothstep(12., 22., distance(aInstance.xyz,uEye));
        float wind = sin(uTime*1.45 + aInstance.x*.70 + aInstance.z*.46 + detail.y*.22);
        world.xz += vec2(.024,.012)*wind*aUv.y*aUv.y;
        world.y = aInstance.y + (world.y-aInstance.y) * scale;
        world.xz = mix(aInstance.xz, world.xz, scale);
    }
    vWorld = world.xyz;
    vNormal = normalize(transpose(inverse(mat3(uModel))) * n);
    vUv = aUv;
    if (uSurface == 1 || uSurface == 2 || uSurface == 3 || uSurface == 15) {
        vec3 face = abs(vNormal);
        vec2 plane = face.y >= max(face.x,face.z) ? vec2(world.x,-world.z) :
                     face.x > face.z ? vec2(-world.z,world.y) : world.xy;
        vUv = plane * (uSurface == 1 ? .5 : uSurface == 2 ? .25 : .40);
    }
    vDetail = detail;
    gl_Position = uViewProjection * world;
    vLine = vec2(0.0);
    if (uSurface == 16)
    {
        vec3 centre = aPosition - aNormal * (aUv.x * aUv.y);
        vec4 clip = uViewProjection * vec4(centre, 1.0);
        vec4 along = uViewProjection * vec4(aJoints.xyz, 0.0);
        vec2 screenAxis = (along.xy * clip.w - clip.xy * along.w) * uViewport;
        vec2 perpendicular = vec2(-screenAxis.y, screenAxis.x) / max(length(screenAxis), 0.0001);
        // The main view has a 60 degree vertical field of view.
        float halfWidth = min(50.0, 0.8660254 * uViewport.y * aUv.y / max(clip.w, 0.1));
        float offset = aUv.x * (halfWidth + 0.75);
        clip.xy += perpendicular * (2.0 * offset / uViewport) * clip.w;
        gl_Position = clip;
        vWorld = centre;
        vLine = vec2(offset, halfWidth);
    }
}
