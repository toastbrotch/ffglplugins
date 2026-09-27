/*{
  "DESCRIPTION": "Sandwich Supernova - a raymarched, spinning sandwich (pick a recipe and a bread, or let it cycle) while its own ingredients burst out of a tunnel behind it, one ingredient per seeded wedge of the burst. Depth of field in a second pass.",
  "CREDIT": "Claude Fable 5.1",
  "CATEGORIES": ["generator", "3d", "food"],
  "INPUTS": [
    { "NAME": "sandwich", "TYPE": "long", "DEFAULT": 0, "VALUES": [0,1,2,3,4,5,6,7,8],
      "LABELS": ["Auto cycle","BLT","Club","Cheeseburger","Ham & Swiss on rye","Veggie","Breakfast bun","PB&J","Chef's random (seed)"] },
    { "NAME": "bread", "TYPE": "long", "DEFAULT": 0, "VALUES": [0,1,2,3,4,5],
      "LABELS": ["Recipe default","White","Whole wheat","Marble rye","Sesame bun","Toasted white"] },
    { "NAME": "seed", "TYPE": "float", "DEFAULT": 3.0, "MIN": 0.0, "MAX": 100.0 },
    { "NAME": "spin", "TYPE": "float", "DEFAULT": 0.45, "MIN": -2.0, "MAX": 2.0 },
    { "NAME": "burstSpeed", "TYPE": "float", "DEFAULT": 1.0, "MIN": 0.0, "MAX": 3.0 },
    { "NAME": "explode", "TYPE": "float", "DEFAULT": 0.0, "MIN": 0.0, "MAX": 1.0 },
    { "NAME": "backdrop", "TYPE": "color", "DEFAULT": [0.03, 0.40, 0.52, 1.0] }
  ],
  "PASSES": [ { "TARGET": "tab", "FLOAT": true, "FILTER": "NEAREST" }, { "TARGET": "scene", "FLOAT": true }, { } ]
}*/

// Everything edible here is ONE generic "slab" SDF (footprint x thickness, with folds, rings,
// holes, waves, droop and a dome) driven by a parameter block per ingredient; all the character
// lives in the once-per-pixel material function. That keeps the unrolled march loops small.
//
// The sandwich is a y-stack, so the map only evaluates the layer whose y-interval contains p and
// clamps the step to the ray's exit from that interval. The burst does the same with (ring, z) cells
// of a polar tunnel: the camera sits on the tunnel axis, so a ray never changes angular sector.

#define TAU 6.2831853
#define CAMZ 7.0
#define FOCAL 1.65
#define R0 1.9
#define RW 2.2
#define SZ 5.6
#define ZFAR 52.0
#define PERIOD 7.5

// ---- per-frame scene description (built once per pixel in setup) ----
float gNW;                                               // number of burst wedges (unique ingredients)
vec4 gSelA, gSelB;                                       // layer thresholds as read back by the scene pass
mat4 gCM; vec4 gCE, gCF; vec3 gQ; float gSc;             // parameter block + local point of the last map call
float gYlo, gYhi;                                        // current y extent of the (possibly mid-cascade) stack
float gFront, gFd; vec3 gWA, gWB;                        // morph front along the tunnel; (count, rotation, flip) of the old / new wedge sets
mat3 gSR; vec3 gPos; float gPop;                         // sandwich rotation / position / pop scale
vec2 gSw; float gZ; float gSeedB; float gWRot; float gWFlip;

// ---- marching state + captures written by the last slab() call ----
vec3 gRd; float gClamp; float gDs;
vec3 gLp; float gD2, gSide, gYs, gMat, gSd;
mat3 gR;

float h11(float n){ return fract(sin(n*12.9898)*43758.5453); }
float h21(vec2 p){ p=fract(p*vec2(123.34,456.21)); p+=dot(p,p+45.32); return fract(p.x*p.y); }
float h31(vec3 p){ p=fract(p*vec3(.1031,.1030,.0973)); p+=dot(p,p.yzx+33.33); return fract((p.x+p.y)*p.z); }
float vnoise(vec3 x){
  vec3 i=floor(x), f=fract(x); f=f*f*(3.-2.*f);
  return mix(mix(mix(h31(i),h31(i+vec3(1,0,0)),f.x),mix(h31(i+vec3(0,1,0)),h31(i+vec3(1,1,0)),f.x),f.y),
             mix(mix(h31(i+vec3(0,0,1)),h31(i+vec3(1,0,1)),f.x),mix(h31(i+vec3(0,1,1)),h31(i+vec3(1,1,1)),f.x),f.y),f.z);
}
float fbm(vec3 p){ return .5*vnoise(p)+.3*vnoise(p*2.03+7.)+.2*vnoise(p*4.1+13.); }

// M = [A: hx,hz,h,roundness | B: waveAmp,waveFreq,edgeGrow,dome(+up/-down) | C: foldX,foldX2,foldZ,intervalBelow | D: yCentre or scale, intervalAbove, material, seed]
// E = ringSpacing, holeFreq, droop, cornerRadius      F = cos, sin, outlineWobble, domeRadius
float slab(vec3 p, mat4 M, vec4 E, vec4 F){
  vec4 A=M[0], B=M[1], C=M[2], D=M[3];
  float py=p.y;
  p.y*= B.w<0. ? -1. : 1.;
  float hd=abs(B.w);
  p.xz=vec2(F.x*p.x-F.y*p.z, F.y*p.x+F.x*p.z);
  p.xz-=(vec2(fract(D.w*7.31),fract(D.w*13.7))-.5)*.07;
  if(C.x>0.) p.x=abs(p.x)-C.x;
  if(C.y>0.) p.x=abs(p.x)-C.y;
  if(C.z>0.) p.z=abs(p.z)-C.z;
  float r=length(p.xz);
  vec2 q=abs(p.xz)-A.xy+E.w;
  float dB=length(max(q,0.))+min(max(q.x,q.y),0.)-E.w;
  float dE=(length(p.xz/A.xy)-1.)*min(A.x,A.y);
  float d2=mix(dB,dE,A.w);
  float ang=atan(p.z,p.x);
  d2+=F.z*(sin(ang*5.+D.w*40.)*.6+sin(ang*9.-D.w*23.)*.4);
  float eg=smoothstep(-.55,0.,d2/min(A.x,A.y));
  if(E.x>0.) d2=max(d2,abs(mod(r,E.x)-.5*E.x)-.3*E.x);
  if(E.y>0.){
    vec2 g=p.xz*E.y+D.w*7.; vec2 ci=floor(g);
    d2=max(d2,(.1+.3*h21(ci+D.w)-length(g-ci-.5))/E.y);
  }
  float w=B.x*sin(B.y*p.z+D.w*30.+1.6*sin(B.y*.55*p.x+D.w*17.))*mix(1.,eg,B.z)+.45*B.x*eg*B.z*sin(B.y*2.3*p.x-D.w*50.+2.*p.z)-E.z*eg*eg;
  float k=clamp(1.-r*r/(F.w*F.w),0.,1.);
  float y2=p.y-w-hd*(k-1.)*.5;
  float th=A.z+hd*k*.5;
  float er=min(A.z*.9,.05);
  vec2 w2=vec2(d2,abs(y2)-th)+er;
  float d=min(max(w2.x,w2.y),0.)+length(max(w2,0.))-er;
  d=max(d,max(py-D.y,-py-C.w)+.006);
  gLp=vec3(p.x,y2,p.z); gD2=d2; gSide=smoothstep(-.012,.012,w2.x-w2.y); gYs=y2; gMat=D.z; gSd=D.w;
  return d;
}

void fetch(float x){
  gCM=mat4(IMG_PIXEL(tab,vec2(x,.5)),IMG_PIXEL(tab,vec2(x,1.5)),IMG_PIXEL(tab,vec2(x,2.5)),IMG_PIXEL(tab,vec2(x,3.5)));
  gCE=IMG_PIXEL(tab,vec2(x,4.5)); gCF=IMG_PIXEL(tab,vec2(x,5.5));
}

float mapS(vec3 p){
  fetch(.5+dot(step(gSelA,vec4(p.y)),vec4(1.))+dot(step(gSelB,vec4(p.y)),vec4(1.)));
  vec4 D=gCM[3];
  gQ=p-vec3(0.,D.x,0.);
  float d=slab(gQ,gCM,gCE,gCF)*.75;
  gDs=d;
  if(gClamp>.5){
    float tb=1e3;
    if(gRd.y>1e-4) tb=(D.x+D.y-p.y)/gRd.y;
    else if(gRd.y<-1e-4) tb=(D.x-gCM[2].w-p.y)/gRd.y;
    if(tb<0.) tb=1e3;
    d=min(d,tb+.004);
  }
  return d;
}

float mapB(vec3 p){
  p.xy=vec2(gSw.x*p.x-gSw.y*p.y, gSw.y*p.x+gSw.x*p.y);
  float r=length(p.xy);
  float j=clamp(floor((r-R0)/RW),0.,2.);
  float rc=R0+(j+.5)*RW;
  float ns=6.+4.*j;
  float a=atan(p.y,p.x)/TAU+.5;
  float k=min(floor(a*ns),ns-1.);
  float hl=h21(vec2(k,j)+gSeedB);
  float zz=p.z-gZ*(.75+.5*hl)+hl*SZ*9.;
  float m=floor(zz/SZ);
  float lz=zz-(m+.5)*SZ;
  float hi=h21(vec2(k+17.*j,m)+gSeedB*1.7);
  float ac=(k+.5)/ns;
  // pieces the front has passed already belong to the next sandwich; they shrink to nothing as it crosses them
  float fd=(m+.5)*SZ+gZ*(.75+.5*hl)-hl*SZ*9.-gFront;
  vec3 wv= fd<0. ? gWB : gWA;
  float wa=fract((wv.z>.5?1.-ac:ac)+wv.y);
  float wi=min(floor(wa*wv.x),wv.x-1.);
  fetch((fd<0. ? 20.5 : 10.5)+wi);
  gFd=fd;
  float th=(ac-.5)*TAU;
  vec2 er=vec2(cos(th),sin(th));
  vec3 q=vec3(p.xy-rc*er-vec2(-er.y,er.x)*(hi-.5)*1.3-er*(fract(hi*9.7)-.5)*.4, lz);   // scatter within the cell so lanes don't read as poles
  float a1=hi*TAU+gZ*.11*(.5+hl)+TIME*(.3+.5*hi), a2=hl*TAU+TIME*(.25+.4*fract(hi*5.3));
  float c1=cos(a1),s1=sin(a1),c2=cos(a2),s2=sin(a2);
  gR=mat3(1.,0.,0., 0.,c1,s1, 0.,-s1,c1)*mat3(c2,0.,-s2, 0.,1.,0., s2,0.,c2);
  gSc=gCM[3].x*(.85+.3*fract(hi*3.1))*max(smoothstep(.2,3.5,abs(fd)),.03);
  gQ=gR*q/gSc;
  float d=slab(gQ,gCM,gCE,gCF)*gSc*.75;
  gDs=d;
  if(gClamp>.5){
    float tz=(lz+.5*SZ)/max(-gRd.z,1e-3);
    float rr=length(gRd.xy);
    float tr=(R0+(j+1.)*RW-r)/max(rr,1e-4);
    if(r<R0) tr=(R0-r)/max(rr,1e-4);
    d=min(d,min(tz,tr)+.02);
  }
  return d;
}

// ---- ingredient table ----
// 1 lettuce 2 tomato 3 bacon 4 cheddar 5 swiss 6 ham 7 red onion 8 pickle 9 patty 10 fried egg 11 bread(bt)
// 12 mayo 13 mustard 14 ketchup 15 peanut butter 16 grape jelly
void ingr(float ty, float bt, float pos, float sd, out mat4 M, out vec4 E, out vec4 F){
  vec4 A=vec4(1.,1.,0.,0.), B=vec4(0.,1.,0.,0.), C=vec4(0.);
  E=vec4(0.,0.,0.,.05);
  float ow=0., dr=1., rotA=TAU, rot0=0., mt=ty;
  vec4 bA=vec4(1.,1.04,.12,0.); float bcr=.22;
  if(bt<3.5&&bt>2.5){ bA=vec4(1.22,.86,.11,.7); bcr=.3; }
  else if(bt>3.5&&bt<4.5){ bA=vec4(1.1,1.1,.09,1.); }
  if(ty<.5){ }
  else if(ty<1.5){ A=vec4(1.27,1.2,.02,1.); B=vec4(.085,6.2,.8,0.); ow=.1; }
  else if(ty<2.5){ A=vec4(.47,.47,.06,1.); C=vec4(.47,0.,.47,0.); ow=.008; }
  else if(ty<3.5){ A=vec4(.2,1.13,.026,0.); B=vec4(.06,8.,0.,0.); C=vec4(.5,.25,0.,0.); E.w=.07; ow=.014; rotA=.25; }
  else if(ty<4.5){ A=vec4(.9,.9,.022,0.); B=vec4(.012,3.,0.,0.); E=vec4(0.,0.,.05,.04); rotA=.3; rot0=.785; }
  else if(ty<5.5){ A=vec4(.9,.9,.024,0.); B=vec4(.012,3.,0.,0.); E=vec4(0.,2.4,.05,.04); rotA=.3; rot0=.785; }
  else if(ty<6.5){ A=vec4(1.12,1.06,.03,1.); B=vec4(.04,4.2,.7,0.); ow=.05; }
  else if(ty<7.5){ A=vec4(.97,.97,.035,1.); E.x=.2; }
  else if(ty<8.5){ A=vec4(.2,.2,.03,1.); B=vec4(.012,42.,0.,0.); C=vec4(.5,.25,.3,0.); }
  else if(ty<9.5){ A=vec4(1.06,1.06,.12,1.); ow=.03; }
  else if(ty<10.5){ A=vec4(.96,.9,.02,1.); B=vec4(.02,5.,.6,.11); ow=.1; dr=.3; }
  else if(ty<11.5){
    mt=20.+bt; rotA=.12; A=bA; E.w=bcr; ow=.012;
    if(bt>3.5&&bt<4.5){ dr=1.1; B.w= pos>.5 ? .44 : (pos<-.5 ? -.14 : 0.); }
  }
  else {
    float thick= ty>14.5 ? .034 : .016;                       // peanut butter and jelly go on thick
    A=vec4(bA.xy*(ty>14.5 ? .97 : .9),thick,max(bA.w,.45)); B=vec4(.012,5.,0.,0.); E=vec4(0.,0.,.025,.3); ow=.07; rotA=.2;
  }
  float ra=(h11(sd*3.7+1.)-.5)*rotA+rot0;
  M=mat4(A,B,C,vec4(0.,0.,mt,sd));
  F=vec4(cos(ra),sin(ra),ow,dr);
}

// half height of a layer's y-interval (thickness + dome + room for its waves)
float Hof(float ty, float bt, float pos){
  float h=.132;
  if(ty<.5) h=0.;
  else if(ty<1.5) h=.108; else if(ty<2.5) h=.072; else if(ty<3.5) h=.087; else if(ty<4.5) h=.07;
  else if(ty<5.5) h=.072; else if(ty<6.5) h=.078; else if(ty<7.5) h=.047; else if(ty<8.5) h=.053;
  else if(ty<9.5) h=.132; else if(ty<10.5) h=.105;
  else if(ty>14.5) h=.058;
  else if(ty>11.5) h=.04;
  else if(bt>2.5&&bt<3.5) h=.122;
  else if(bt>3.5&&bt<4.5) h=.102+(pos>.5 ? .22 : (pos<-.5 ? .07 : 0.));
  return h;
}

// pass 0: the whole scene description as a tiny lookup table. Each texel works out only the one block it holds.
//   x 0..8 sandwich layers | x 10..17 burst wedges of the outgoing recipe | x 20..27 of the incoming one | x 30 scalars
void tablePass(){
  vec2 fc=floor(gl_FragCoord.xy);
  if(fc.x>30.5||fc.y>5.5){ gl_FragColor=vec4(0.); return; }
  float cyc=floor(TIME/PERIOD), tc=TIME-cyc*PERIOD;
  bool nextWave= tc>PERIOD-.8;
  float front=-ZFAR-6.+(nextWave ? tc-(PERIOD-.8) : tc+.8)*42.;
  bool isA=(fc.x>9.5&&fc.x<18.5)||(fc.x>29.5&&fc.y>2.5&&fc.y<3.5);
  bool isB=(fc.x>19.5&&fc.x<28.5)||(fc.x>29.5&&fc.y>3.5);
  float c=cyc;
  if(isA) c= nextWave ? cyc : cyc-1.;
  if(isB) c= nextWave ? cyc+1. : cyc;
  float sel=float(sandwich), sseed=seed, anim=0.;
  if(sandwich==0){ anim=1.; sel=1.+mod(c,9.); if(sel>7.5){ sel=8.; sseed=seed+c*1.37; } }
  else front=1e3;

  vec4 P=vec4(12.,1.,2.,3.), Q=vec4(11.,0.,0.,0.);
  float bt=5.;
  if(sel<1.5){ }
  else if(sel<2.5){ P=vec4(12.,6.,2.,11.); Q=vec4(1.,3.,11.,0.); bt=1.; }
  else if(sel<3.5){ P=vec4(1.,9.,4.,2.); Q=vec4(7.,14.,13.,11.); bt=4.; }
  else if(sel<4.5){ P=vec4(13.,6.,5.,8.); Q=vec4(1.,11.,0.,0.); bt=3.; }
  else if(sel<5.5){ P=vec4(12.,1.,2.,7.); Q=vec4(8.,5.,11.,0.); bt=2.; }
  else if(sel<6.5){ P=vec4(14.,4.,3.,10.); bt=4.; }
  else if(sel<7.5){ P=vec4(15.,16.,11.,0.); Q=vec4(0.); bt=1.; }
  else {
    float nf=2.+floor(h11(sseed+.3)*4.99);
    vec4 sA=vec4(1.,2.,3.,4.), sB=vec4(5.,6.,7.,8.);
    vec4 hA=fract(sin((sA*5.1+sseed*1.7)*12.9898)*43758.5453), hB=fract(sin((sB*5.1+sseed*1.7)*12.9898)*43758.5453);
    vec4 kA=fract(hA*17.3), kB=fract(hB*17.3);
    vec4 rA=mix(1.+floor(hA*9.99),12.+floor(fract(hA*7.7)*2.99),step(.78,kA));
    vec4 rB=mix(1.+floor(hB*9.99),12.+floor(fract(hB*7.7)*2.99),step(.78,kB));
    // slot s holds a filling while s<=nf, the top bread at s==nf+1, nothing above
    P=mix(rA,vec4(11.),step(nf+.5,sA))*step(sA,vec4(nf+1.5));
    Q=mix(rB,vec4(11.),step(nf+.5,sB))*step(sB,vec4(nf+1.5));
    bt=1.+floor(h11(sseed*2.3+9.)*4.99);
  }
  if(bread>0) bt=float(bread);
  float rseed=sseed+sel*11.3+bt*3.1;
  float T[9];
  T[0]=11.; T[1]=P.x; T[2]=P.y; T[3]=P.z; T[4]=P.w; T[5]=Q.x; T[6]=Q.y; T[7]=Q.z; T[8]=Q.w;
  float nl=0.;
  for(int i=0;i<9;i++) if(T[i]>.5) nl+=1.;

  // walk the stack once: interval of every layer (rest height + cascade offset), and which
  // ingredient is the w-th unique one. Layers drop in bottom-first and lift off top-first; an
  // offset can never be less than the layer below's, so intervals stay ordered and a bounce shoves upward.
  float Sel[9];
  float seen=0., yc=0., yr=0., offP=0., nw=0.;
  float ty=0., tpos=0., tyc=0., tUp=0., tLo=0., tsd=.5;
  float wq=fc.x-(fc.x>19.5 ? 20. : 10.);
  float eg=explode*.2;
  for(int i=0;i<9;i++){
    float fi=float(i);
    float pos= i==0 ? -1. : (fi>nl-1.5 ? 1. : 0.);
    float has=step(.5,T[i]);
    float H=Hof(T[i],bt,pos)+eg*has;
    float tl=tc-fi*.11;
    float u=clamp(tl/.62,0.,1.);
    float off=7.5*(1.-u)*(1.-u)+.1*abs(sin(max(tl-.62,0.)*9.))*exp(-max(tl-.62,0.)*5.);
    float v=max(tc-(PERIOD-1.1)-(nl-1.-fi)*.07,0.)/.55;
    off=max((off+9.*v*v)*anim,offP)*has+offP*(1.-has);
    float s=off-offP; offP=off;
    Sel[i]= has>.5 ? yc : 1e4;
    float bit=exp2(T[i]);
    bool dup= has<.5 || mod(floor(seen/bit+.001),2.)>.5;
    seen+= dup ? 0. : bit;
    bool mine= fc.x<9.5 ? abs(fc.x-fi)<.5 : (!dup && abs(wq-nw)<.5);
    if(mine){ ty=T[i]; tpos=pos; tyc=yc+s+H; tUp=H; tLo=H+s; tsd=h11(rseed+fi*1.9)+.01; }
    if(!dup) nw+=1.;
    yc+=s+2.*H; yr+=2.*H;
  }
  float yh=.5*yr;
  mat4 M; vec4 E, F;
  ingr(ty,bt,tpos,tsd,M,E,F);
  if(fc.x<9.5) M=mat4(M[0],M[1],vec4(M[2].xyz,tLo),vec4(tyc-yh,tUp,M[3].z,tsd));
  else {
    // a wedge of the burst: the same ingredient as one loose piece, normalised in size (sauces fly as dollops)
    vec4 A=M[0], B=M[1];
    if(ty>10.5) B.w= (bt>3.5&&bt<4.5) ? .44 : 0.;
    float sc=clamp(.74/max(A.x,A.y),.74,2.6);
    if(ty>11.5){ A=vec4(.5,.46,.05,1.); B=vec4(.03,4.,0.,.34); F=vec4(1.,0.,.1,.52); E=vec4(0.,0.,0.,.05); sc=1.05; }
    M=mat4(A,B,vec4(0.,0.,0.,10.),vec4(sc,10.,M[3].z,tsd));
    F=vec4(1.,0.,F.z,F.w);
  }
  vec4 o=F;
  if(fc.y<.5) o=M[0]; else if(fc.y<1.5) o=M[1]; else if(fc.y<2.5) o=M[2]; else if(fc.y<3.5) o=M[3];
  else if(fc.y<4.5) o=E;
  if(fc.x>29.5){
    if(fc.y<.5) o=vec4(Sel[1],Sel[2],Sel[3],Sel[4])-yh;
    else if(fc.y<1.5) o=vec4(Sel[5],Sel[6],Sel[7],Sel[8])-yh;
    else if(fc.y<2.5) o=vec4(-yh,yc-yh,front,0.);
    else o=vec4(max(nw,1.),h11(rseed+4.4),h11(rseed+8.8),0.);
  }
  gl_FragColor=o;
}

void motion(){
  float sa=TIME*spin+.6, ta=.36+.07*sin(TIME*.7);
  if(sandwich==0){
    // a twirl as it assembles, a wind-up as it lifts off
    float tc=mod(TIME,PERIOD);
    float u=1.-clamp(tc/1.7,0.,1.), v=max(tc-(PERIOD-1.1),0.);
    sa+=5.*u*u*u+3.*v*v;
  }
  float cs=cos(sa), ss=sin(sa), ct=cos(ta), st=sin(ta);
  gSR=mat3(1.,0.,0., 0.,ct,st, 0.,-st,ct)*mat3(cs,0.,-ss, 0.,1.,0., ss,0.,cs);
  gPos=vec3(0.,.06*sin(TIME*1.1)-.05,0.);

  float sw=TIME*.05;
  gSw=vec2(cos(sw),sin(sw));
  gZ=TIME*burstSpeed*5.5;
}

// ---- materials: albedo / roughness / translucency / bump, from the slab-local captures ----
void material(float id, vec3 lp, float d2, float side, float ys, float sd,
              out vec3 alb, out float rough, out float sss, out float bAmp, out float bFreq){
  float r=length(lp.xz), ang=atan(lp.z,lp.x);
  float n1=fbm(lp*5.+sd*10.), n2=vnoise(lp*23.+sd*5.);
  alb=vec3(.8); rough=.6; sss=0.; bAmp=.3; bFreq=20.;
  if(id<1.5){
    // pale rib down the middle, chevron veins off it, dark frilly rim, blotchy chlorophyll
    float edge=smoothstep(-.85,-.03,d2);
    float rib=smoothstep(.10,.0,abs(lp.z))*smoothstep(1.1,.2,abs(lp.x));
    float vv=abs(fract(lp.x*2.6-abs(lp.z)*1.7+.25*n1)-.5);
    float veins=smoothstep(.07,.0,vv)*smoothstep(1.15,.35,r)*.55;
    alb=mix(vec3(.34,.52,.10),vec3(.07,.24,.03),edge);
    alb=mix(alb,vec3(.13,.36,.05),smoothstep(.35,.7,n1)*.6);
    alb=mix(alb,vec3(.74,.82,.46),max(rib*.9,veins));
    alb=mix(alb,vec3(.62,.74,.30),smoothstep(.55,.1,r)*.5);
    alb*=.8+.35*n2;
    rough=.62-.12*rib; sss=.85; bAmp=1.8-1.2*rib; bFreq=6.5;
  } else if(id<2.5){
    float lobe=abs(sin(ang*2.+sd*9.+.4*sin(ang*3.)));
    float ch=smoothstep(.15,.5,lobe)*smoothstep(.35,.30,r)*smoothstep(.08,.15,r);
    alb=mix(vec3(.70,.05,.03),vec3(.86,.13,.06),n1);
    alb=mix(alb,vec3(.80,.22,.05),ch*.8);
    vec2 g=vec2(ang*2.2,r*9.)+n2*.3;
    float sdl=length((fract(g)-.5)*vec2(1.,.55));
    alb=mix(alb,vec3(.93,.72,.33),smoothstep(.17,.10,sdl)*ch*.85);
    alb=mix(alb,vec3(.90,.45,.30),smoothstep(.09,.03,r)*.7);
    alb=mix(alb,vec3(.60,.03,.02),side);
    rough=mix(.18,.28,side); sss=.5; bAmp=.12+.5*ch; bFreq=26.;
  } else if(id<3.5){
    float f=vnoise(vec3(lp.x*13.+sd*31.,lp.z*.9+sin(lp.x*9.)*.2,sd*3.));
    float fat=smoothstep(.56,.66,f);
    vec3 meat=mix(vec3(.46,.09,.05),vec3(.24,.05,.025),smoothstep(.3,.75,n1));      // rendered dark where it crisped
    vec3 fatc=mix(vec3(.93,.70,.42),vec3(.78,.45,.20),smoothstep(.35,.8,n2));       // golden, not white
    alb=mix(meat,fatc,fat);
    alb=mix(alb,meat*1.3,smoothstep(.75,.9,vnoise(vec3(lp.x*40.,lp.z*3.,sd)))*fat*.6); // lean threads in the fat
    alb=mix(alb,vec3(.13,.04,.015),smoothstep(-.07,0.,d2)*.75);
    rough=mix(.3,.22,fat); sss=.2+.35*fat; bAmp=1.1; bFreq=19.;
  } else if(id<4.5){
    alb=vec3(1.,.50,.04)*(.9+.15*n1); rough=.33; sss=.7; bAmp=.06;
  } else if(id<5.5){
    alb=vec3(1.,.83,.40)*(.9+.15*n1); rough=.33; sss=.75; bAmp=.06;
  } else if(id<6.5){
    alb=mix(vec3(.90,.47,.48),vec3(.98,.72,.70),smoothstep(.3,.7,n1));
    alb=mix(alb,vec3(.70,.30,.27),smoothstep(-.05,-.005,d2)*.8);
    rough=.36; sss=.45; bAmp=.25; bFreq=11.;
  } else if(id<7.5){
    float rl=mod(r,.2)/.2;
    alb=mix(vec3(.97,.93,.96),vec3(.52,.08,.36),smoothstep(.55,.78,rl));
    alb=mix(alb,vec3(.80,.62,.80),.3*smoothstep(.45,.2,rl));
    rough=.25; sss=.75; bAmp=.1;
  } else if(id<8.5){
    alb=mix(vec3(.66,.72,.26),vec3(.22,.34,.06),smoothstep(-.045,-.012,d2));
    float sr=smoothstep(.05,.08,r)*smoothstep(.14,.11,r);
    alb=mix(alb,vec3(.86,.88,.55),sr*smoothstep(.2,.7,abs(sin(ang*4.)))*.8);
    alb=mix(alb,vec3(.20,.32,.05),side);
    rough=.2; sss=.6; bAmp=.15;
  } else if(id<9.5){
    alb=mix(vec3(.17,.08,.04),vec3(.40,.21,.10),n1);
    alb*=.5+.8*n2;
    rough=.5; bAmp=1.4; bFreq=17.;
  } else if(id<10.5){
    float yk=smoothstep(.31,.27,r);
    alb=mix(vec3(.97,.96,.91),vec3(1.,.55,.02),yk);
    alb=mix(alb,vec3(.72,.45,.16),(1.-yk)*smoothstep(-.16,0.,d2)*(.3+.7*n1));
    rough=mix(.33,.1,yk); sss=.35; bAmp=.2*(1.-yk); bFreq=9.;
  } else if(id<16.5){
    float sw=sin(lp.x*7.+3.*n1+1.5*sin(lp.z*6.));                  // knife marks
    if(id<12.5){ alb=vec3(.97,.94,.82); rough=.4; sss=.6; }
    else if(id<13.5){ alb=mix(vec3(.90,.60,.03),vec3(.42,.25,.04),smoothstep(.74,.82,n2)*.7); rough=.33; sss=.3; }
    else if(id<14.5){ alb=vec3(.56,.025,.012); rough=.15; sss=.35; }
    else if(id<15.5){ alb=vec3(.60,.36,.13); rough=.42; sss=.2; }
    else { alb=vec3(.17,.012,.13); rough=.07; sss=.7; }
    alb*=.88+.12*sw; bAmp=1.; bFreq=5.;
  } else {
    float bt=id-20.;
    vec3 crumb=vec3(.95,.87,.69), crust=vec3(.62,.34,.11);
    float toast=0., pk=1.;
    if(bt<2.5&&bt>1.5){ crumb=vec3(.70,.49,.27); crust=vec3(.38,.20,.08); }
    else if(bt<3.5&&bt>2.5){
      float swl=sin(r*8.+ang*2.+n1*5.+sd*20.);
      crumb=mix(vec3(.82,.66,.44),vec3(.44,.26,.13),smoothstep(-.2,.2,swl)); crust=vec3(.27,.145,.07); pk=.55;
    }
    else if(bt<4.5&&bt>3.5){ crumb=vec3(.97,.89,.68); crust=vec3(.78,.42,.10); }
    else if(bt>4.5) toast=1.;
    // open crumb: big irregular cells, medium pores, fine pits - each a little darker and warmer inside
    float p1=smoothstep(.60,.78,vnoise(lp*9.+sd*9.));
    float p2=smoothstep(.56,.78,vnoise(lp*21.+4.));
    float p3=smoothstep(.55,.8,vnoise(lp*47.));
    float pores=(p1*.5+p2*.35+p3*.22)*pk;
    crumb=mix(crumb,crumb*vec3(.55,.42,.30),min(pores,1.));
    if(bt>1.5&&bt<2.5) crumb=mix(crumb,vec3(.30,.18,.08),smoothstep(.72,.8,vnoise(lp*46.+3.))*.8);
    // toast browns the middle of the face and leaves a pale ring inside the crust
    float tz=toast*smoothstep(.28,.62,n1*.55+.6*smoothstep(-.06,-.4,d2)-.25*p1);
    crumb=mix(crumb,crumb*vec3(.74,.45,.20),tz);
    float bun= (bt>3.5&&bt<4.5) ? 1. : 0.;
    float cm=max(side,smoothstep(-.075,-.03,d2));
    cm=mix(cm,max(cm,step(0.,ys)),bun);
    crust*=.75+.5*n1;
    crust=mix(crust,crust*.6,smoothstep(.4,.9,abs(lp.y)/.12)*(1.-bun)*side);   // bake darkens toward the slice's face edges
    // sesame
    vec2 g=lp.xz*6.5+sd*3.; vec2 ci=floor(g);
    float hs=h21(ci+sd*13.); float sa2=hs*TAU;
    vec2 sq=g-ci-.5-(vec2(hs,fract(hs*7.7))-.5)*.5;
    sq=vec2(cos(sa2)*sq.x-sin(sa2)*sq.y, sin(sa2)*sq.x+cos(sa2)*sq.y);
    float ses=smoothstep(.17,.12,length(sq*vec2(1.,2.1)))*step(.3,hs)*bun*step(0.,ys)*smoothstep(1.05,.85,r);
    alb=mix(crumb,crust,cm);
    alb=mix(alb,vec3(.98,.90,.70),ses);
    rough=mix(.92,mix(.55,.3,bun),cm); sss=.2;
    bAmp=mix(1.9,.4,cm)+ses*1.5; bFreq=mix(21.,9.,cm);
  }
}

vec3 bgcol(vec2 uv){
  float rho=length(uv), ang=atan(uv.y,uv.x);
  vec3 bd=backdrop.rgb;
  float rays=smoothstep(-.1,.1,sin(ang*12.+TIME*.6));
  vec3 c=mix(bd*.20,bd*.62+.01,rays);
  c*=1.25-.95*smoothstep(.05,1.,rho);
  vec3 glow=mix(vec3(1.,.90,.70),bd+.5,.3);
  float pulse=exp(-pow((gFront+ZFAR-4.)*.05,2.));
  c+=glow*(1.0*exp(-rho*4.2)+1.6*exp(-rho*11.))*(1.+.8*pulse)+glow*pulse*.22*exp(-rho*1.6);
  return c;
}

vec3 envc(vec3 r, vec3 L){
  vec3 e=mix(vec3(.10,.085,.07),backdrop.rgb*.55+.18,smoothstep(-.4,.7,r.y));
  e+=smoothstep(.80,.97,dot(r,L))*vec3(1.,.93,.82)*3.5;
  e+=pow(max(-r.z,0.),3.)*vec3(1.,.95,.85)*1.3;
  return e;
}

vec3 lightIt(vec3 pw, vec3 n, vec3 rd, vec3 alb, float rough, float sss, float sh, float ao){
  vec3 L1=normalize(vec3(-.55,.78,.6)), k1=vec3(1.,.91,.78)*2.7;
  vec3 L2=normalize(vec3(0.,1.5,-40.)-pw), k2=mix(vec3(1.,.95,.85),backdrop.rgb+.45,.4)*1.6;
  float ndl=max(dot(n,L1),0.), ndb=max(dot(n,L2),0.), ndv=max(dot(n,-rd),0.);
  vec3 amb=mix(vec3(.30,.22,.16),backdrop.rgb*.22+vec3(.50,.47,.43),n.y*.5+.5)*.6;
  vec3 c=alb*(k1*ndl*sh+amb*ao+k2*ndb*.55*(.4+.6*ao));
  // thin-sheet translucency: light arriving on the far side leaks through, tinted by the flesh
  vec3 tint=mix(alb*alb*1.5,alb,.45);
  c+=sss*tint*(k1*max(-dot(n,L1),0.)*.4*(.3+.7*sh)+k2*max(-dot(n,L2),0.)*.55+k1*.05*(1.-ndl))*(.5+.5*ao);
  float a=rough*rough;
  float ex=min(2./(a*a)-2.,1500.);
  vec3 h1=normalize(L1-rd), h2=normalize(L2-rd);
  float fr=.04+.96*pow(1.-ndv,5.);
  c+=k1*sh*ndl*(ex+2.)/8.*pow(max(dot(n,h1),0.),ex)*(.04+.3*pow(1.-max(dot(h1,-rd),0.),5.));
  c+=k2*ndb*(ex+2.)/8.*pow(max(dot(n,h2),0.),ex)*.05*ao;
  c+=envc(reflect(rd,n),L1)*fr*(1.-rough)*(1.-rough)*ao;
  c+=k2*pow(1.-ndv,3.)*.22*ao*(.3+.7*smoothstep(-.2,.6,-n.z));
  return c;
}

// perturb a normal (given in texture space) by the gradient of a small noise field
vec3 bump(vec3 n, vec3 tp, float amp, float fq){
  vec2 e=vec2(.35/fq,0.);
  float b0=vnoise(tp*fq)+.5*vnoise(tp*fq*2.3+5.);
  vec3 g=vec3(vnoise((tp+e.xyy)*fq)+.5*vnoise((tp+e.xyy)*fq*2.3+5.),
              vnoise((tp+e.yxy)*fq)+.5*vnoise((tp+e.yxy)*fq*2.3+5.),
              vnoise((tp+e.yyx)*fq)+.5*vnoise((tp+e.yyx)*fq*2.3+5.))-b0;
  g/=e.x*fq;
  g-=n*dot(g,n);
  return normalize(n-g*amp*.09);
}

void scenePass(){
  motion();
  gSelA=IMG_PIXEL(tab,vec2(30.5,.5)); gSelB=IMG_PIXEL(tab,vec2(30.5,1.5));
  vec4 g2=IMG_PIXEL(tab,vec2(30.5,2.5));
  gYlo=g2.x; gYhi=g2.y; gFront=g2.z;
  gWA=IMG_PIXEL(tab,vec2(30.5,3.5)).xyz; gWB=IMG_PIXEL(tab,vec2(30.5,4.5)).xyz;
  gSeedB=floor(h11(seed+.7)*90.); gPop=1.;
  vec2 uv=(gl_FragCoord.xy-.5*RENDERSIZE.xy)/RENDERSIZE.y;
  vec3 ro=vec3(0.,0.,CAMZ), rd=normalize(vec3(uv,-FOCAL));
  vec3 bg=bgcol(uv);
  vec3 col=bg; float coc=.011;

  // hit record, shaded once below whichever march produced it
  float kind=0.;                       // 0 nothing, 1 sandwich, 2 burst
  vec3 q0=vec3(0.), pw=vec3(0.), pl=vec3(0.);
  mat4 M0=mat4(0.); vec4 E0=vec4(0.), F0=vec4(0.); mat3 R0m=mat3(1.);
  float sh=1., ao=1., fog=1., fd0=1e3;

  // ---------- the sandwich ----------
  if(gPop>.02){
    vec3 rol=((ro-gPos)*gSR)/gPop, rdl=rd*gSR;
    rdl+=vec3(1e-5)*step(abs(rdl),vec3(1e-5));
    vec3 bh=vec3(1.6,.5*(gYhi-gYlo)+.02,1.6), bc=vec3(0.,.5*(gYhi+gYlo),0.);
    vec3 ta=(bc-bh-rol)/rdl, tb=(bc+bh-rol)/rdl;
    vec3 tmn=min(ta,tb), tmx=max(ta,tb);
    float tN=max(max(tmn.x,tmn.y),tmn.z), tF=min(min(tmx.x,tmx.y),tmx.z);
    if(tN<tF && tF>0.){
      float t=max(tN,0.); float hit=-1.;
      gRd=rdl; gClamp=1.;
      for(int i=0;i<56;i++){
        float d=mapS(rol+rdl*t);
        if(gDs<.0015){ hit=t; break; }
        t+=d; if(t>tF) break;
      }
      if(hit>0.){
        kind=1.;
        pl=rol+rdl*hit; q0=gQ; M0=gCM; E0=gCE; F0=gCF;
        pw=gSR*(pl*gPop)+gPos;
      }
    }
  }

  // ---------- the burst ----------
  float rr=length(rd.xy);
  if(kind<.5 && rr>.02 && gPop>.02){
    float t=max(R0/rr,.6), tE=min((R0+3.*RW)/rr,(CAMZ+ZFAR)/(-rd.z));
    float hit=-1.;
    gRd=rd; gClamp=1.;
    for(int i=0;i<48;i++){
      float d=mapB(ro+rd*t);
      if(gDs<.0015*t){ hit=t; break; }
      t+=d; if(t>tE) break;
    }
    if(hit>0.){
      kind=2.;
      pw=ro+rd*hit; q0=gQ; M0=gCM; E0=gCE; F0=gCF; R0m=gR; fd0=gFd;
      fog=exp(-.042*max(-pw.z,0.))*smoothstep(-ZFAR,-ZFAR+16.,pw.z);
      coc=min(.02,abs(1./CAMZ-1./hit)*.21);
    }
  }

  if(kind>.5){
    // normal in slab space, straight from the slab with the parameter block the march left behind
    vec2 e=vec2(1.,-1.)*.0022;
    vec3 n=normalize(e.xyy*slab(q0+e.xyy,M0,E0,F0)+e.yyx*slab(q0+e.yyx,M0,E0,F0)+e.yxy*slab(q0+e.yxy,M0,E0,F0)+e.xxx*slab(q0+e.xxx,M0,E0,F0));
    slab(q0,M0,E0,F0);
    vec3 alb; float rough,sss,bA,bF;
    material(gMat,gLp,gD2,gSide,gYs,gSd,alb,rough,sss,bA,bF);
    ao=.72+.28*smoothstep(-.3,0.,gD2);
    n=bump(n,q0,bA,bF);
    if(kind<1.5){
      gClamp=0.;
      float oc=(.04-mapS(pl+n*.04)/.75)*8.+(.13-mapS(pl+n*.13)/.75)*3.5;
      ao=clamp(1.-oc*.6,0.,1.);
      // soft self-shadow, marched in sandwich space
      vec3 Ll=normalize(vec3(-.55,.78,.6))*gSR;
      float ts=.02;
      vec3 po=pl+n*.012;
      gRd=Ll; gClamp=1.;
      for(int i=0;i<36;i++){
        float d=mapS(po+Ll*ts);
        sh=min(sh,10.*gDs/.75/ts+.22*ts*ts);      // far-off layers (mid-cascade) cast only a faint, wide shadow
        ts+=max(d,.012);
        if(sh<.02||ts>2.6) break;
      }
      sh=clamp(sh,0.,1.); sh=sh*sh*(3.-2.*sh);
      n=gSR*n; coc=0.;
    } else {
      n=n*R0m;
      n=vec3(gSw.x*n.x+gSw.y*n.y, -gSw.y*n.x+gSw.x*n.y, n.z);
    }
    vec3 lit=lightIt(pw,n,rd,alb,rough,sss,sh,ao);
    lit+=vec3(1.,.8,.5)*2.5*exp(-abs(fd0)*.55);          // pieces glow white-hot as the morph front crosses them
    col=mix(bg,lit,fog);
  }
  gl_FragColor=vec4(col,coc);
}

void postPass(){
  vec2 uv=gl_FragCoord.xy/RENDERSIZE.xy;
  vec4 c0=IMG_NORM_PIXEL(scene,uv);
  float rad=max(c0.a,.75/RENDERSIZE.y);
  vec3 acc=c0.rgb; float ws=1.;
  float j=fract(52.9829189*fract(dot(gl_FragCoord.xy,vec2(.06711056,.00583715))))*TAU;
  vec2 asp=vec2(RENDERSIZE.y/RENDERSIZE.x,1.);
  for(int i=0;i<44;i++){
    float fi=float(i)+.5;
    float rr=sqrt(fi/44.)*rad;
    float a=fi*2.39996+j;
    vec4 s=IMG_NORM_PIXEL(scene,uv+rr*vec2(cos(a),sin(a))*asp);
    float w=clamp((max(s.a,.75/RENDERSIZE.y)-rr*.7)/(rr*.3+1e-5),0.,1.);
    w*=1.+.25*dot(s.rgb,vec3(.33));              // brighter samples bloom a little in the bokeh
    acc+=s.rgb*w; ws+=w;
  }
  vec3 col=acc/ws;
  vec2 q=uv-.5;
  col*=1.-.55*dot(q,q)*1.6;
  col*=.95;
  col=(col*(2.51*col+.03))/(col*(2.43*col+.59)+.14);
  col=pow(clamp(col,0.,1.),vec3(1./2.2));
  col=mix(vec3(dot(col,vec3(.299,.587,.114))),col,1.12);
  col=mix(col,col*col*(3.-2.*col),.35);
  col+=(h21(gl_FragCoord.xy+fract(TIME)*91.)-.5)*.012;
  gl_FragColor=vec4(col,1.);
}

void main(){
  if(PASSINDEX==0) tablePass(); else if(PASSINDEX==1) scenePass(); else postPass();
}
