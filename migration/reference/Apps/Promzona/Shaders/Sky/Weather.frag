#version 330 core

in vec3 vWorldPos;
out vec4 FragColor;
uniform vec3 uEye, uSun;
uniform float uTime, uWeatherRain, uWeatherDay, uGoldenLight;
uniform vec3 uFogHorizon, uFogZenith;

float skyHash(vec2 p)
{
    return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453);
}

float skyNoise(vec2 p)
{
    vec2 cell = floor(p);
    vec2 f = fract(p);
    f = f * f * (3.0 - 2.0 * f);
    return mix(mix(skyHash(cell), skyHash(cell + vec2(1, 0)), f.x),
               mix(skyHash(cell + vec2(0, 1)), skyHash(cell + 1.0), f.x), f.y);
}

float clouds(vec2 p)
{
    float value = 0.0;
    float amplitude = 0.53;
    for (int octave = 0; octave < 4; ++octave) {
        value += amplitude * skyNoise(p);
        p = p * 2.03 + vec2(13.2, 7.1);
        amplitude *= 0.48;
    }
    return value;
}

void main()
{
    vec3 ray = normalize(vWorldPos - uEye);
    float altitude = max(0.0, ray.y);
    vec3 sky = mix(uFogHorizon, uFogZenith, pow(altitude, 0.55));

    float sunDot = max(0.0, dot(ray, normalize(uSun)));
    float nearHorizon = smoothstep(0.0, 0.07, altitude) * (1.0 - smoothstep(0.12, 0.58, altitude));
    sky += vec3(0.56, 0.22, 0.045) * uGoldenLight * nearHorizon * pow(sunDot, 3.0);
    vec3 sunlight = mix(vec3(1.0, 0.84, 0.61), vec3(1.0, 0.68, 0.36), uGoldenLight);
    sky += sunlight * (pow(sunDot, 1600.0) * 0.8 + pow(sunDot, 20.0) * 0.12)
         * uWeatherDay * (1.0 - uWeatherRain) * (1.0 - uWeatherRain) * smoothstep(-0.04, 0.025, uSun.y);
    vec3 moon = normalize(vec3(-uSun.x, max(0.22, -uSun.y), -uSun.z));
    float moonDot = max(0.0, dot(ray, moon));
    sky += vec3(0.40, 0.47, 0.57) * (smoothstep(0.99982, 0.99996, moonDot) + 0.025 * pow(moonDot, 80.0))
         * (1.0 - uWeatherDay) * (1.0 - uWeatherRain);

    if (ray.y > 0.015) {
        // Cloud drift follows visual seconds, independently of the accelerated clock.
        vec2 p = ray.xz / (ray.y + 0.18) * 2.3 + vec2(uTime * 0.003, uTime * 0.0012);
        float density = clouds(p);
        float detail = clouds(p + vec2(5.2, 9.1));
        float coverage = mix(smoothstep(0.49, 0.73, density), 0.88 + 0.12 * smoothstep(0.2, 0.8, density), uWeatherRain);
        vec3 cloud = mix(vec3(0.68, 0.72, 0.76), vec3(0.91, 0.92, 0.91), detail);
        cloud = mix(cloud, mix(vec3(0.19, 0.23, 0.28), vec3(0.40, 0.44, 0.48), density), uWeatherRain);
        cloud = mix(vec3(0.012, 0.019, 0.032) * (0.8 + 0.4 * density), cloud, uWeatherDay);
        // Optical depth from the existing cloud samples: thin edges transmit
        // warm sunlight, thicker cloud banks stay cooler against the bright sky.
        float thinEdge = 1.0 - smoothstep(0.53, 0.72, density);
        vec3 sunsetCloud = mix(vec3(0.46, 0.39, 0.48), vec3(1.13, 0.72, 0.43),
                               thinEdge * (0.27 + 0.73 * sunDot) + 0.12 * detail);
        cloud = mix(cloud, sunsetCloud, uGoldenLight);
        sky = mix(sky, cloud, coverage * smoothstep(0.015, 0.28, altitude));

        float stars = step(0.998, skyHash(floor(ray.xz / (altitude + 0.1) * 480.0)))
                    * pow(max(0.0, skyNoise(p * 110.0)), 12.0);
        sky += vec3(0.4, 0.47, 0.6) * stars * (1.0 - uWeatherDay) * (1.0 - uWeatherRain)
             * (1.0 - coverage) * smoothstep(0.12, 0.35, altitude);
    }
    FragColor = vec4(sky, 1.0);
}
