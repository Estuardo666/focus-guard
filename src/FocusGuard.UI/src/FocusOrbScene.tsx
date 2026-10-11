import { useEffect, useRef } from 'react';
import { AddEquation, Color, CustomBlending, OneFactor, ZeroFactor, Mesh, NormalBlending, PerspectiveCamera, Quaternion, Scene, ShaderMaterial, SphereGeometry, Vector3, WebGLRenderer, type IUniform } from 'three';
import type { OrbStyle } from './design-system/palettes';

export type OrbPointer = { x: number; y: number; inside: boolean };

// Ashima / Stefan Gustavson 3D simplex noise (MIT).
const noise = /* glsl */`
vec3 mod289(vec3 x){return x-floor(x*(1.0/289.0))*289.0;}
vec4 mod289(vec4 x){return x-floor(x*(1.0/289.0))*289.0;}
vec4 permute(vec4 x){return mod289(((x*34.0)+1.0)*x);}
vec4 taylorInvSqrt(vec4 r){return 1.79284291400159-0.85373472095314*r;}
float snoise(vec3 v){
  const vec2 C=vec2(1.0/6.0,1.0/3.0);const vec4 D=vec4(0.0,0.5,1.0,2.0);
  vec3 i=floor(v+dot(v,C.yyy));vec3 x0=v-i+dot(i,C.xxx);
  vec3 g=step(x0.yzx,x0.xyz);vec3 l=1.0-g;vec3 i1=min(g.xyz,l.zxy);vec3 i2=max(g.xyz,l.zxy);
  vec3 x1=x0-i1+C.xxx;vec3 x2=x0-i2+C.yyy;vec3 x3=x0-D.yyy;
  i=mod289(i);
  vec4 p=permute(permute(permute(i.z+vec4(0.0,i1.z,i2.z,1.0))+i.y+vec4(0.0,i1.y,i2.y,1.0))+i.x+vec4(0.0,i1.x,i2.x,1.0));
  float n_=0.142857142857;vec3 ns=n_*D.wyz-D.xzx;
  vec4 j=p-49.0*floor(p*ns.z*ns.z);vec4 x_=floor(j*ns.z);vec4 y_=floor(j-7.0*x_);
  vec4 x=x_*ns.x+ns.yyyy;vec4 y=y_*ns.x+ns.yyyy;vec4 h=1.0-abs(x)-abs(y);
  vec4 b0=vec4(x.xy,y.xy);vec4 b1=vec4(x.zw,y.zw);
  vec4 s0=floor(b0)*2.0+1.0;vec4 s1=floor(b1)*2.0+1.0;vec4 sh=-step(h,vec4(0.0));
  vec4 a0=b0.xzyw+s0.xzyw*sh.xxyy;vec4 a1=b1.xzyw+s1.xzyw*sh.zzww;
  vec3 p0=vec3(a0.xy,h.x);vec3 p1=vec3(a0.zw,h.y);vec3 p2=vec3(a1.xy,h.z);vec3 p3=vec3(a1.zw,h.w);
  vec4 norm=taylorInvSqrt(vec4(dot(p0,p0),dot(p1,p1),dot(p2,p2),dot(p3,p3)));
  p0*=norm.x;p1*=norm.y;p2*=norm.z;p3*=norm.w;
  vec4 m=max(0.6-vec4(dot(x0,x0),dot(x1,x1),dot(x2,x2),dot(x3,x3)),0.0);m=m*m;
  return 42.0*dot(m*m,vec4(dot(p0,x0),dot(p1,x1),dot(p2,x2),dot(p3,x3)));
}`;

// uWobble scales the surface ripple, uPuff adds the slow billowing of a cloud.
const vertexShader = /* glsl */`
uniform float uTime;uniform float uBreath;uniform vec3 uPointer;uniform float uHover;uniform float uEnergy;uniform float uWobble;uniform float uPuff;
varying vec3 vNormal;varying vec3 vView;varying float vNoise;varying vec3 vObj;
${noise}
void main(){
  vec3 p=position;
  float n=snoise(normal*1.1+vec3(uTime*0.14));
  float n2=snoise(normal*2.7-vec3(uTime*0.1));
  float pull=pow(max(dot(normal,uPointer),0.0),4.0)*uHover;
  float billow=snoise(normal*1.7+vec3(uTime*0.05,uTime*0.04,0.0))*0.075+snoise(normal*3.4-vec3(uTime*0.035))*0.03;
  p+=normal*((n*0.016+n2*0.006)*(0.6+uEnergy)*uWobble+pull*0.045+billow*uPuff);
  p*=1.0+uBreath*0.032;
  vNoise=n;vObj=position;
  vec4 mv=modelViewMatrix*vec4(p,1.0);
  vView=normalize(-mv.xyz);
  vNormal=normalize(normalMatrix*normal);
  gl_Position=projectionMatrix*mv;
}`;

const header = /* glsl */`
uniform vec3 uA;uniform vec3 uB;uniform vec3 uC;uniform float uTime;uniform float uLight;uniform float uHover;uniform float uBreath;uniform float uEnergy;uniform float uLayer;
varying vec3 vNormal;varying vec3 vView;varying float vNoise;varying vec3 vObj;
${noise}
float fbm(vec3 p){float a=0.5;float s=0.0;for(int i=0;i<3;i++){s+=a*snoise(p);p*=1.9;a*=0.45;}return s;}
vec3 film(float t){return 0.5+0.5*cos(6.28318*(vec3(0.0,0.33,0.67)+t));}
float dither(){return (fract(sin(dot(gl_FragCoord.xy,vec2(12.9898,78.233)))*43758.5453)-0.5)/255.0;}`;

// Liquid pearl: marbled palette body, milky core, thin-film iridescence toward the rim, two highlights.
const pearlFragment = /* glsl */`${header}
void main(){
  float facing=clamp(dot(vNormal,vView),0.0,1.0);
  float fres=pow(1.0-facing,2.6);
  vec3 q=vObj*0.8+vec3(0.0,0.0,uTime*0.05);
  float w=fbm(q+fbm(q*0.8+vec3(uTime*0.04))*1.25);
  vec3 body=mix(uA,uB,smoothstep(-0.45,0.55,w));
  body=mix(body,uC,smoothstep(0.1,0.8,fbm(q*1.2-vec3(uTime*0.035)))*0.6);
  vec3 pearl=mix(vec3(0.985,0.975,1.0),body,0.4+0.42*fres+0.08*uHover);
  vec3 iri=film(fres*1.35+w*0.35+uTime*0.015);
  pearl=mix(pearl,pearl*0.62+iri*0.5,fres*0.42);
  vec3 key=normalize(vec3(-0.5,0.65,0.6));
  float spec=pow(max(dot(reflect(-key,vNormal),vView),0.0),70.0);
  float soft=pow(max(dot(reflect(-normalize(vec3(0.6,-0.35,0.55)),vNormal),vView),0.0),14.0)*0.14;
  float rim=smoothstep(0.5,1.0,fres);
  vec3 c=pearl+spec*0.6+soft+rim*mix(uC,vec3(1.0),0.45)*0.3-0.04*uLight*(1.0-fres);
  gl_FragColor=vec4(c+dither(),1.0);
}`;

// Glass shell over the pearl: only its iridescent rim and a sharp highlight are visible.
const pearlShellFragment = /* glsl */`${header}
void main(){
  float facing=clamp(dot(vNormal,vView),0.0,1.0);
  float fres=pow(1.0-facing,3.2);
  vec3 iri=film(fres*1.1+vNoise*0.2+uTime*0.02);
  float spec=pow(max(dot(reflect(-normalize(vec3(-0.45,0.7,0.55)),vNormal),vView),0.0),180.0);
  vec3 c=mix(vec3(1.0),iri,0.55)*fres*(0.55-0.25*uLight)+spec*0.9;
  gl_FragColor=vec4(c*clamp(fres*0.9+spec,0.0,1.0)+dither(),0.0);
}`;

// Voice: a sky-blue sphere with white currents drifting upward inside it, a lit crown and a milky rim; no hard highlight.
const voiceFragment = /* glsl */`${header}
void main(){
  float facing=clamp(dot(vNormal,vView),0.0,1.0);
  float fres=pow(1.0-facing,2.0);
  vec2 uv=vNormal.xy;
  vec3 deep=mix(vec3(0.16,0.42,0.98),uB,0.25);
  vec3 sky=mix(vec3(0.52,0.78,1.0),uA,0.15);
  vec3 c=mix(deep,sky,smoothstep(-0.9,0.7,uv.y));
  float warp=fbm(vec3(uv*1.3,uTime*0.09));
  float flow=fbm(vec3(uv.x*1.5+warp*0.8,uv.y*1.1-uTime*0.2,uTime*0.07));
  float wisps=smoothstep(0.0,0.5,flow)*(0.75+0.25*uEnergy);
  c=mix(c,vec3(0.99,0.995,1.0),wisps*0.75);
  c=mix(c,deep*0.9,smoothstep(-0.15,-0.6,flow)*0.35);
  c+=vec3(0.9,0.95,1.0)*smoothstep(0.25,1.0,uv.y)*0.18;
  c+=pow(facing,5.0)*0.05*(1.0+uBreath*0.6);
  c=mix(c,vec3(0.94,0.97,1.0),fres*0.45);
  gl_FragColor=vec4(c+dither(),1.0);
}`;

// Lava: dark, slowly creeping crust split by incandescent cracks; the heat swells with each breath.
const lavaFragment = /* glsl */`${header}
void main(){
  float facing=clamp(dot(vNormal,vView),0.0,1.0);
  float fres=pow(1.0-facing,2.4);
  vec3 q=vObj*1.05+vec3(0.0,uTime*0.022,uTime*0.01);
  float n=fbm(q+fbm(q*0.7+vec3(uTime*0.018))*1.5);
  float crack=pow(clamp(1.0-abs(n)*2.4,0.0,1.0),5.0);
  float pools=smoothstep(0.25,0.85,fbm(q*2.1-vec3(uTime*0.03)))*0.45;
  float heat=clamp((crack+pools)*(0.82+0.18*uBreath)+uHover*0.06,0.0,1.0);
  vec3 crust=mix(vec3(0.045,0.018,0.016),vec3(0.2,0.06,0.035),smoothstep(-0.6,0.6,n));
  crust*=0.75+0.35*pow(max(dot(vNormal,normalize(vec3(-0.4,0.7,0.6))),0.0),2.0);
  vec3 ember=mix(vec3(0.78,0.1,0.015),vec3(1.0,0.56,0.08),smoothstep(0.2,0.75,heat));
  ember=mix(ember,vec3(1.0,0.9,0.55),smoothstep(0.8,1.0,heat));
  vec3 c=mix(crust,ember,smoothstep(0.05,0.6,heat));
  c+=vec3(1.0,0.32,0.05)*fres*0.55;
  float spec=pow(max(dot(reflect(-normalize(vec3(-0.5,0.65,0.6)),vNormal),vView),0.0),40.0)*(1.0-heat);
  gl_FragColor=vec4(c+spec*0.12+dither(),1.0);
}`;

// Heat haze around the lava: an additive orange halo that breathes.
const lavaShellFragment = /* glsl */`${header}
void main(){
  float facing=clamp(dot(vNormal,vView),0.0,1.0);
  float fres=pow(1.0-facing,2.2);
  vec3 c=vec3(1.0,0.36,0.06)*fres*(0.5+0.22*uBreath)*(1.0-0.45*uLight);
  gl_FragColor=vec4(c+dither(),0.0);
}`;

// Cloud: soft density with no hard silhouette. Each layer is a little larger and fainter, so the edge reads as a blur.
const cloudFragment = /* glsl */`${header}
void main(){
  float facing=clamp(dot(vNormal,vView),0.0,1.0);
  vec3 q=vObj*1.5+vec3(uTime*0.022,uTime*0.016,-uTime*0.01)+uLayer*0.7;
  float d=fbm(q+fbm(q*1.25)*0.9)*0.5+0.5;
  float edge=smoothstep(0.0,0.85-uLayer*0.2,facing);
  float alpha=edge*smoothstep(0.2,0.75,d+0.3*facing)*(1.0-uLayer*0.45);
  float top=dot(vNormal,normalize(vec3(-0.3,0.85,0.45)))*0.5+0.5;
  vec3 shade=mix(mix(uB,vec3(0.78,0.82,0.92),0.55),vec3(1.0),smoothstep(0.35,0.95,d)*0.85);
  shade*=0.8+0.28*top;
  shade=mix(shade,mix(uA,uB,0.5),uLight*0.32);
  shade+=uHover*0.04;
  gl_FragColor=vec4(shade+dither(),clamp(alpha*(0.92-uLight*0.08),0.0,1.0));
}`;

// Adds light only (colour ONE + ONE) and leaves the canvas alpha untouched, so glows never grey a light page.
const lightOnly = { blending: CustomBlending, blendEquation: AddEquation, blendSrc: OneFactor, blendDst: OneFactor, blendSrcAlpha: ZeroFactor, blendDstAlpha: OneFactor } as const;

type StyleRecipe = { wobble: number; puff: number; breathScale: number; core: string; shells: readonly { fragment: string; scale: number; layer: number; glow: boolean }[]; translucent?: boolean };
/**
 * Each orb has its own material, ripple and breathing pace. Gas breathes slowest, viscous lava slower than water,
 * the voice orb at the base pace with a livelier ripple.
 */
const recipes: Record<OrbStyle, StyleRecipe> = {
  pearl: { wobble: 1, puff: 0, breathScale: 1, core: pearlFragment, shells: [{ fragment: pearlShellFragment, scale: 1.045, layer: 0, glow: true }] },
  voice: { wobble: 1.9, puff: 0, breathScale: 1, core: voiceFragment, shells: [] },
  lava: { wobble: 0.7, puff: 0, breathScale: 1.5, core: lavaFragment, shells: [{ fragment: lavaShellFragment, scale: 1.06, layer: 0, glow: true }] },
  cloud: { wobble: 0.5, puff: 1, breathScale: 2, core: cloudFragment, translucent: true, shells: [{ fragment: cloudFragment, scale: 1.07, layer: 1, glow: false }, { fragment: cloudFragment, scale: 1.14, layer: 2, glow: false }] },
};

/**
 * A breathing, cursor-aware orb rendered with WebGL. It is purely decorative: the timer text and
 * controls live in the DOM, so screen readers and keyboard users never depend on this canvas.
 */
export default function FocusOrbScene({ colors, pointer, breathSeconds, energy, still, light, orbStyle = 'pearl', onFailure }: {
  colors: readonly [string, string, string]; pointer: { current: OrbPointer }; breathSeconds: number; energy: number;
  still: boolean; light: boolean; orbStyle?: OrbStyle; onFailure(): void;
}) {
  const host = useRef<HTMLDivElement>(null);
  const uniforms = useRef<Record<string, IUniform> | null>(null);
  const settings = useRef({ breathSeconds, energy, still, style: orbStyle });
  settings.current = { breathSeconds, energy, still, style: orbStyle };
  const renderOnce = useRef<() => void>(() => undefined);
  const applyStyle = useRef<(style: OrbStyle) => void>(() => undefined);

  useEffect(() => {
    const element = host.current;
    if (!element) return;
    let renderer: WebGLRenderer;
    try {
      renderer = new WebGLRenderer({ alpha: true, antialias: true, powerPreference: 'low-power' });
    } catch {
      onFailure();
      return;
    }
    renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
    renderer.setClearColor(0x000000, 0);
    const canvas = renderer.domElement;
    canvas.setAttribute('aria-hidden', 'true');
    element.appendChild(canvas);

    const scene = new Scene();
    const camera = new PerspectiveCamera(34, 1, 0.1, 10);
    camera.position.z = 4.05;
    const geometry = new SphereGeometry(1, 160, 160);
    const shared: Record<string, IUniform> = {
      uTime: { value: 0 }, uBreath: { value: 0 }, uPointer: { value: new Vector3(0, 0, 1) }, uHover: { value: 0 },
      uEnergy: { value: settings.current.energy }, uLight: { value: light ? 1 : 0 },
      uA: { value: new Color(colors[0]) }, uB: { value: new Color(colors[1]) }, uC: { value: new Color(colors[2]) },
      uWobble: { value: 1 }, uPuff: { value: 0 },
    };
    uniforms.current = shared;
    const mesh = new Mesh(geometry);
    scene.add(mesh);
    let materials: ShaderMaterial[] = [];
    let breathScale = 1;

    // Swaps the orb's materials in place; the old ones are disposed so switching styles never leaks GPU programs.
    applyStyle.current = (style: OrbStyle) => {
      const recipe = recipes[style];
      for (const child of [...mesh.children]) mesh.remove(child);
      for (const material of materials) material.dispose();
      const translucent = recipe.translucent ? { transparent: true, depthWrite: false, blending: NormalBlending } : {};
      const core = new ShaderMaterial({ uniforms: { ...shared, uLayer: { value: 0 } }, vertexShader, fragmentShader: recipe.core, ...translucent });
      materials = [core];
      mesh.material = core;
      recipe.shells.forEach((shell, index) => {
        const material = new ShaderMaterial({ uniforms: { ...shared, uLayer: { value: shell.layer } }, vertexShader, fragmentShader: shell.fragment,
          transparent: true, depthWrite: false, ...(shell.glow ? lightOnly : { blending: NormalBlending }) });
        const layer = new Mesh(geometry, material);
        layer.scale.setScalar(shell.scale);
        layer.renderOrder = index + 1;
        mesh.add(layer);
        materials.push(material);
      });
      shared.uWobble!.value = recipe.wobble;
      shared.uPuff!.value = recipe.puff;
      breathScale = recipe.breathScale;
      renderOnce.current();
    };

    const resize = () => {
      const size = Math.max(1, Math.min(element.clientWidth, element.clientHeight));
      // Drawing buffer only; CSS sizes the canvas so the host can shrink freely.
      renderer.setSize(size, size, false);
      renderOnce.current();
    };
    const observer = typeof ResizeObserver === 'undefined' ? null : new ResizeObserver(resize);
    observer?.observe(element);

    const pointerDirection = new Vector3();
    const inverse = new Quaternion();
    const rotation = { x: 0, y: 0 };
    let hover = 0;
    let frame = 0;
    let last = performance.now();
    let elapsed = 0;
    const draw = (delta: number) => {
      const { breathSeconds: period, energy: currentEnergy, still: isStill } = settings.current;
      if (!isStill) elapsed += delta;
      const target = pointer.current;
      const inside = target.inside && !isStill;
      // Critically damped follow: the orb leans toward the cursor with weight instead of snapping.
      const follow = 1 - Math.exp(-delta * 3.2);
      rotation.x += ((inside ? -target.y * 0.38 : 0) - rotation.x) * follow;
      rotation.y += ((inside ? target.x * 0.5 : 0) + (isStill ? 0 : elapsed * 0.05) - rotation.y) * follow;
      hover += ((inside ? 1 : 0) - hover) * (1 - Math.exp(-delta * 2.4));
      mesh.rotation.set(rotation.x, rotation.y, 0);
      inverse.copy(mesh.quaternion).invert();
      pointerDirection.set(target.x, target.y, 0.9).normalize().applyQuaternion(inverse);
      (shared.uPointer!.value as Vector3).copy(pointerDirection);
      shared.uHover!.value = hover;
      shared.uTime!.value = elapsed;
      shared.uEnergy!.value += (currentEnergy - (shared.uEnergy!.value as number)) * follow;
      shared.uBreath!.value = isStill ? 0 : Math.sin((elapsed / (period * breathScale)) * Math.PI * 2);
      renderer.render(scene, camera);
    };
    renderOnce.current = () => draw(0);
    applyStyle.current(settings.current.style);
    let stillFrameDrawn = false;
    const loop = (now: number) => {
      const delta = Math.min((now - last) / 1000, 0.05);
      last = now;
      // A still orb (reduced motion) is drawn once per change instead of every frame.
      if (settings.current.still) {
        if (!stillFrameDrawn && !document.hidden) { draw(1); stillFrameDrawn = true; }
      } else {
        stillFrameDrawn = false;
        if (!document.hidden) draw(delta);
      }
      frame = requestAnimationFrame(loop);
    };
    const lost = (event: Event) => { event.preventDefault(); cancelAnimationFrame(frame); onFailure(); };
    canvas.addEventListener('webglcontextlost', lost);
    resize();
    frame = requestAnimationFrame(loop);
    return () => {
      cancelAnimationFrame(frame);
      observer?.disconnect();
      canvas.removeEventListener('webglcontextlost', lost);
      geometry.dispose();
      for (const material of materials) material.dispose();
      renderer.dispose();
      canvas.remove();
      uniforms.current = null;
      renderOnce.current = () => undefined;
      applyStyle.current = () => undefined;
    };
    // The renderer is created once; colours, style and settings flow through refs and uniforms.
  }, []);

  useEffect(() => { applyStyle.current(orbStyle); }, [orbStyle]);

  useEffect(() => {
    const shared = uniforms.current;
    if (!shared) return;
    (shared.uA!.value as Color).set(colors[0]);
    (shared.uB!.value as Color).set(colors[1]);
    (shared.uC!.value as Color).set(colors[2]);
    shared.uLight!.value = light ? 1 : 0;
    renderOnce.current();
  }, [colors, light]);

  return <div ref={host} className="orb-scene" data-orb={orbStyle} aria-hidden="true"/>;
}
