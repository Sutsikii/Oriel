import "./style.css";
import * as THREE from "three";
import { GLTFLoader } from "three/examples/jsm/loaders/GLTFLoader.js";
import { OrbitControls } from "three/examples/jsm/controls/OrbitControls.js";
import { Models } from "./oriel";

const canvas = document.getElementById("scene") as HTMLCanvasElement;
const stats = document.getElementById("stats")!;
const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
renderer.setPixelRatio(devicePixelRatio);
const scene = new THREE.Scene();
scene.background = new THREE.Color(0x101318);
scene.add(new THREE.HemisphereLight(0xffffff, 0x334455, 2));
const sun = new THREE.DirectionalLight(0xffffff, 2.5);
sun.position.set(3, 4, 5);
scene.add(sun);
const camera = new THREE.PerspectiveCamera(45, 1, 0.01, 1000);
camera.position.set(0, 0, 4);
const controls = new OrbitControls(camera, canvas);
controls.enableDamping = true;

let model: THREE.Object3D | null = null;
const loader = new GLTFLoader();

/** Shows the model of a .glb's bytes, as they came from C#: no copy, no base64. */
async function show(bytes: Uint8Array, what: string): Promise<void> {
  const started = performance.now();
  const gltf = await loader.parseAsync(bytes.buffer.slice(bytes.byteOffset, bytes.byteOffset + bytes.byteLength) as ArrayBuffer, "");
  if (model) scene.remove(model);
  model = gltf.scene;
  // Centered, and sized to the view.
  const box = new THREE.Box3().setFromObject(model);
  const size = box.getSize(new THREE.Vector3()).length();
  model.position.sub(box.getCenter(new THREE.Vector3()));
  camera.position.setLength(size * 1.4);
  scene.add(model);
  let triangles = 0;
  model.traverse((o) => {
    const mesh = o as THREE.Mesh;
    if (mesh.isMesh) triangles += (mesh.geometry.index?.count ?? mesh.geometry.attributes.position.count) / 3;
  });
  stats.textContent = `${what}: ${(bytes.byteLength / 1048576).toFixed(1)} MB, ${Math.round(triangles).toLocaleString()} triangles, parsed in ${Math.round(performance.now() - started)} ms`;
}

async function knot(segments: number): Promise<void> {
  const started = performance.now();
  const bytes = await Models.knot(segments, 64);
  stats.textContent = `received in ${Math.round(performance.now() - started)} ms`;
  await show(bytes, "Torus knot");
}

document.getElementById("detail")!.addEventListener("change", (e) => void knot(Number((e.target as HTMLInputElement).value)));
document.getElementById("open")!.addEventListener("click", async () => {
  const bytes = await Models.open();
  if (bytes.byteLength > 0) await show(bytes, "Model");
});

function resize(): void {
  renderer.setSize(innerWidth, innerHeight, false);
  camera.aspect = innerWidth / innerHeight;
  camera.updateProjectionMatrix();
}
addEventListener("resize", resize);
resize();
renderer.setAnimationLoop(() => {
  controls.update();
  if (model) model.rotation.y += 0.003;
  renderer.render(scene, camera);
});

void knot(512);
