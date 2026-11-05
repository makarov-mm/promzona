#version 330 core
in vec3 vWorld,vNormal;
in vec2 vUv;
out vec4 fragColor;
uniform sampler2D uColor,uNormal;
uniform int uHasNormal,uSkin,uSurface;
uniform vec3 uEye,uSun,uFog,uTint;
uniform float uDay,uRain,uEmissive,uAlpha,uTime;
uniform float uRoughness,uMetallic;
void main(){vec4 tex=texture(uColor,vUv);if(tex.a<.05)discard;
 vec3 n=normalize(vNormal);if(!gl_FrontFacing)n=-n;
 // World-space detail is filtered by pixel footprint to avoid shimmering at distance.
 // Derivatives must use the detail's phase, not metres: a 50 rad/m pattern
 // already aliases at a few centimetres per pixel, long before a 1m footprint.
 float phaseWidth=max(fwidth(vWorld.x*51.7),fwidth(vWorld.z*43.1));
 float fade=1-smoothstep(1.0,3.14159,phaseWidth);
 float grain=sin(vWorld.x*51.7)*sin(vWorld.z*43.1);
 if(uSurface==1)tex.rgb*=.90+.10*sin(vWorld.x*.12)*sin(vWorld.z*.09)+.04*grain*fade;
 if(uSurface==2)tex.rgb*=.90+.08*grain*fade;
 if(uSurface==3){vec2 grid=abs(fract(vWorld.xz*.5)-.5);vec2 width=max(fwidth(vWorld.xz*.5),vec2(.008));float seam=max(smoothstep(.48-width.x,.5,grid.x),smoothstep(.48-width.y,.5,grid.y));tex.rgb*=1-.2*seam;}
 if(uSurface==5)tex.rgb*=.93+.07*grain*fade;
 if(uSurface==4)n=normalize(vec3(.035*cos(vWorld.x*1.7+vWorld.z*.7+uTime*.8),1,.045*sin(vWorld.z*1.1-vWorld.x*.3+uTime*.65)));
 if(uHasNormal!=0){vec3 q1=dFdx(vWorld),q2=dFdy(vWorld);vec2 st1=dFdx(vUv),st2=dFdy(vUv);vec3 t=q1*st2.y-q2*st1.y;float l=length(t);if(l>1e-6){t/=l;vec3 b=-normalize(cross(n,t));vec3 m=texture(uNormal,vUv).xyz*2-1;n=normalize(mat3(t,b,n)*m);}}
 vec3 albedo=pow(max(tex.rgb*uTint,vec3(.001)),vec3(2.2));vec3 v=normalize(uEye-vWorld),l=normalize(uSun);vec3 h=normalize(v+l);
 float diffuse=max(dot(n,l),0),rough=mix(clamp(uRoughness,.05,1),.25,uRain*max(n.y,0));float spec=pow(max(dot(n,h),0),mix(18,100,1-rough))*(.08+.3*uRain+.3*uMetallic);
 float golden=exp(-pow((uSun.y-.15)*4,2))*uDay;vec3 light=mix(vec3(1,.97,.91),vec3(1,.64,.34),golden*.55);
 vec3 color=albedo*(.16+.32*uDay+light*diffuse*(.1+.8*uDay))+spec*light*uDay+albedo*uEmissive;
 if(uSurface==4){float fresnel=.04+.70*pow(1-max(dot(n,v),0),5);vec3 sky=pow(uFog,vec3(2.2))*(.7+.3*n.y);color=mix(color,sky,fresnel);}
 float distance=length(uEye-vWorld);float fog=1-exp(-distance*(.00052+.0028*uRain));color=mix(color,pow(uFog,vec3(2.2)),fog);
 color=color/(color+vec3(1));fragColor=vec4(pow(color,vec3(1/2.2)),tex.a*uAlpha);}
