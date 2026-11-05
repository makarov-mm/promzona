#version 330 core
in vec2 vUv;out vec4 FragColor;
uniform sampler2D uColor,uDepth,uAo;uniform vec2 uInvSize,uClip;
float viewZ(vec2 uv){float d=texture(uDepth,uv).r;return uClip.x*uClip.y/(uClip.y-d*(uClip.y-uClip.x));}
float luma(vec3 c){return dot(c,vec3(.299,.587,.114));}
void main(){
 vec3 c=texture(uColor,vUv).rgb;
 vec3 nw=texture(uColor,vUv+vec2(-1.,-1.)*uInvSize).rgb,ne=texture(uColor,vUv+vec2(1.,-1.)*uInvSize).rgb;
 vec3 sw=texture(uColor,vUv+vec2(-1.,1.)*uInvSize).rgb,se=texture(uColor,vUv+vec2(1.,1.)*uInvSize).rgb;
 float lm=luma(c),lo=min(lm,min(min(luma(nw),luma(ne)),min(luma(sw),luma(se)))),hi=max(lm,max(max(luma(nw),luma(ne)),max(luma(sw),luma(se))));
 if(hi-lo>max(.035,hi*.13)){
 vec2 dir=vec2(-((luma(nw)+luma(ne))-(luma(sw)+luma(se))),((luma(nw)+luma(sw))-(luma(ne)+luma(se))));
 float reduce=max((luma(nw)+luma(ne)+luma(sw)+luma(se))*.03125,.0078125);
 dir=clamp(dir/(min(abs(dir.x),abs(dir.y))+reduce),vec2(-6.),vec2(6.))*uInvSize;
 vec3 a=.5*(texture(uColor,vUv+dir*(-1./6.)).rgb+texture(uColor,vUv+dir*(1./6.)).rgb);
 vec3 b=.5*a+.25*(texture(uColor,vUv-dir*.5).rgb+texture(uColor,vUv+dir*.5).rgb);
 c=luma(b)<lo||luma(b)>hi?a:b;
 }
 float center=viewZ(vUv),ao=0.,weight=0.;
 for(int y=-1;y<=1;y++)for(int x=-1;x<=1;x++){
 vec2 uv=vUv+vec2(x,y)*uInvSize*2.;float w=exp(-abs(viewZ(uv)-center)*6.)*(x==0&&y==0?2.:1.);
 ao+=texture(uAo,uv).r*w;weight+=w;
 }
 c*=ao/max(weight,.001);
 // The material shaders already compress direct highlights. Retain their midtones,
 // compress only the HDR shoulder, so lamps have colour instead of clipped white.
 c=c/(1.+max(vec3(0.),c-.8)*.25);
 FragColor=vec4(c,1.);
}
