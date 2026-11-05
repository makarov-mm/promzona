layout(location = 3) in vec4 aJoints;
layout(location = 4) in vec4 aWeights;
uniform sampler2D uBones;
uniform int uSkin;
uniform float uPose, uPreviousPose, uPoseBlend;
#ifndef CITY_SKIN_POSES
#define CITY_SKIN_POSES vec3(uPreviousPose,uPose,uPoseBlend)
#endif
mat4 cityJoint(int joint, int pose)
{
    int x = joint * 4;
    return mat4(texelFetch(uBones, ivec2(x, pose), 0), texelFetch(uBones, ivec2(x + 1, pose), 0),
                texelFetch(uBones, ivec2(x + 2, pose), 0), texelFetch(uBones, ivec2(x + 3, pose), 0));
}
mat4 cityPose(float pose)
{
    int last = textureSize(uBones, 0).y - 1;
    int p = clamp(int(pose), 0, last), base = (p / 64) * 64;
    int next = min(last, base + (p + 1 - base) % 64);
    float blend = fract(pose);
    mat4 result = mat4(0.);
    for (int i = 0; i < 4; i++)
    {
        if (aWeights[i] > 0.)
        {
            int j = int(aJoints[i]);
            result += aWeights[i] * (cityJoint(j, p) * (1. - blend) + cityJoint(j, next) * blend);
        }
    }
    return result;
}
mat4 citySkin()
{
    if (uSkin == 0)
        return mat4(1.);
    vec3 p = CITY_SKIN_POSES;
    if (p.z >= .999)
        return cityPose(p.y);
    if (p.z <= .001)
        return cityPose(p.x);
    return cityPose(p.x) * (1. - p.z) + cityPose(p.y) * p.z;
}
