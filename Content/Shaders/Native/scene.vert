#version 330 core
layout(location=0) in vec3 aPosition;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec2 aUv;
layout(location=3) in vec4 aJoints;
layout(location=4) in vec4 aWeights;
uniform mat4 uModel, uViewProjection;
uniform sampler2D uBones;
uniform int uSkin;
uniform float uPose, uPreviousPose, uPoseBlend;
out vec3 vWorld, vNormal;
out vec2 vUv;
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
    vec4 world = uModel * p;
    vWorld = world.xyz;
    vNormal = normalize(transpose(inverse(mat3(uModel))) * n);
    vUv = aUv;
    vDetail = aJoints;
    gl_Position = uViewProjection * world;
}
