uniform vec4 uWheels[6];
uniform int uWheelCount, uTextured;
uniform float uWheelTravel, uSteer;
uniform vec3 uVehicleScale;
void wheelPose(inout vec3 p, inout vec3 n, vec2 uv)
{
    // The shared glTF importer flips V. Legacy palette tags therefore arrive
    // negative; true-UV VAZ tags deliberately arrive positive and keep fractional V.
    float tag = uTextured == 1 ? uv.y : 1. - uv.y;
    if (tag < 2.)
        return;
    int index = int((tag - 2.) / (uTextured == 1 ? 2. : 1.));
    if (index >= uWheelCount)
        return;
    vec4 w = uWheels[index];
    float spin = uWheelTravel / max(.12, abs(w.w) * uVehicleScale.y), steer = w.w > 0. ? uSteer : 0.;
    mat3 roll = mat3(1, 0, 0, 0, cos(spin), sin(spin), 0, -sin(spin), cos(spin));
    mat3 yaw = mat3(cos(steer), 0, -sin(steer), 0, 1, 0, sin(steer), 0, cos(steer));
    p = w.xyz + (yaw * roll * ((p - w.xyz) * uVehicleScale)) / uVehicleScale;
    n = normalize((yaw * roll * (n / uVehicleScale)) * uVehicleScale);
}
