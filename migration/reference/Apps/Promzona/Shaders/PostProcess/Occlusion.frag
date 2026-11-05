#version 330 core
in vec2 vUv;out float FragColor;
uniform sampler2D uDepth;uniform vec2 uInvSize;uniform vec4 uLens;uniform int uSamples;
// Reconstruct at the actual depth texel centre. Half-resolution AO coordinates
// fall on full-resolution texel boundaries; mixing those rays with nearest
// depth samples produced horizontal self-shadow bands even on a flat plane.
ivec2 depthPixel(vec2 uv){return clamp(ivec2(uv*vec2(textureSize(uDepth,0))),ivec2(0),textureSize(uDepth,0)-1);}
vec3 position(ivec2 pixel){
 pixel=clamp(pixel,ivec2(0),textureSize(uDepth,0)-1);
 float d=texelFetch(uDepth,pixel,0).r,z=uLens.z*uLens.w/(uLens.w-d*(uLens.w-uLens.z));
 vec2 uv=(vec2(pixel)+.5)/vec2(textureSize(uDepth,0));
 return vec3((uv*2.-1.)*z/uLens.xy,-z);
}
void main(){
 ivec2 pixel=depthPixel(vUv);float d=texelFetch(uDepth,pixel,0).r;vec3 p=position(pixel);if(d>.999999||-p.z>160.){FragColor=1.;return;}
 vec3 l=position(pixel-ivec2(1,0)),r=position(pixel+ivec2(1,0));
 vec3 b=position(pixel-ivec2(0,1)),t=position(pixel+ivec2(0,1));
 vec3 dx=abs(r.z-p.z)<abs(p.z-l.z)?r-p:p-l,dy=abs(t.z-p.z)<abs(p.z-b.z)?t-p:p-b;
 vec3 n=normalize(cross(dx,dy));if(dot(n,-p)<0.)n=-n;
 vec3 tangent=normalize(cross(n,abs(n.z)<.9?vec3(0,0,1):vec3(0,1,0))),bitangent=cross(n,tangent);
 float phase=fract(sin(dot(floor(gl_FragCoord.xy),vec2(12.9898,78.233)))*43758.5453)*6.2831853;
 float occ=0.;float radius=.95;
 for(int i=0;i<12;i++){if(i>=uSamples)break;float f=(float(i)+.5)/float(uSamples),angle=phase+float(i)*2.399963;
 vec3 direction=tangent*cos(angle)*sqrt(1.-f*f)+bitangent*sin(angle)*sqrt(1.-f*f)+n*f;
 vec3 q=p+direction*radius*(.18+.82*f);
 vec2 uv=q.xy*uLens.xy/(-q.z)*.5+.5;
 if(any(lessThan(uv,vec2(.001)))||any(greaterThan(uv,vec2(.999))))continue;
 vec3 surface=position(depthPixel(uv));
 float sampleZ=-surface.z,range=1.-smoothstep(radius*.5,radius*2.,length(surface-p));
 // A neighbouring texel of the same plane is not an occluder. At grazing
 // angles its centre can be in front of q solely due to pixel quantisation.
 occ+=step(sampleZ,-q.z-.035)*step(.025,dot(surface-p,n))*range;
 }
 FragColor=1.-.56*occ/float(uSamples)*(1.-smoothstep(70.,160.,-p.z));
}
