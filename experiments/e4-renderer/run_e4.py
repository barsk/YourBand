"""Experiment E4: the same Rocketbox guitarist in URP and HDRP, compared for looks and frame time.

  python run_e4.py [setup] [bench] [URP] [HDRP]      (default: setup and bench for both)

setup: copies the avatar, the Mixamo clip and the E4 scripts into each throwaway project and runs the editor in
       batch mode (E4Setup.Run): materials, scene, and a benchmark player in <project>/Builds/E4.
bench: runs each benchmark player; screenshots and results.json land in results/<pipeline>/.
The projects live outside the repository (Unity projects grow to gigabytes): see PROJECTS.
"""
import json
import os
import shutil
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
EDITOR = r"C:\Program Files\Unity\Hub\Editor\6000.6.7f1\Editor\Unity.exe"
PROJECTS = {
    "URP": r"C:\Users\krist\Documents\Unity\GuitarTutor-E4\E4-URP",
    "HDRP": r"C:\Users\krist\Documents\Unity\GuitarTutor-E4\E4-HDRP",
}
AVATAR = os.path.join(REPO, "mockup", "avatars", "Male_Adult_04")
CLIP = os.path.join(REPO, "mockup", "mocap", "guitar_playing_111490901.fbx")


def copy_inputs(pipeline, project):
    e4 = os.path.join(project, "Assets", "E4")
    for sub in ("Export", "Textures"):
        shutil.copytree(os.path.join(AVATAR, sub), os.path.join(e4, "Avatar", sub), dirs_exist_ok=True)
    os.makedirs(os.path.join(e4, "Mocap"), exist_ok=True)
    shutil.copy2(CLIP, os.path.join(e4, "Mocap"))
    for folder, files in (("Editor", ["E4Setup.cs", "E4Pipeline.%s.cs" % pipeline]), ("Runtime", ["E4Bench.cs"])):
        os.makedirs(os.path.join(e4, folder), exist_ok=True)
        for f in files:
            shutil.copy2(os.path.join(HERE, folder, f), os.path.join(e4, folder, f))


def start_setup(pipeline, project):
    copy_inputs(pipeline, project)
    log = os.path.join(project, "Logs", "e4_setup.log")
    os.makedirs(os.path.dirname(log), exist_ok=True)
    cmd = [EDITOR, "-batchmode", "-quit", "-projectPath", project, "-executeMethod", "E4Setup.Run", "-logFile", log]
    return subprocess.Popen(cmd), log


def report_setup(pipeline, proc, log, started):
    code = proc.wait()
    lines = open(log, encoding="utf-8", errors="replace").read().splitlines()
    notes = [l for l in lines if l.startswith("E4:")]
    errors = [l for l in lines if "error CS" in l or l.startswith("Exception") or "Error building" in l][:10]
    print("%s setup: exit %d in %.0f s" % (pipeline, code, time.time() - started))
    for l in notes + errors:
        print("   ", l)
    return code == 0


def bench(pipeline, project):
    out = os.path.join(HERE, "results", pipeline)
    os.makedirs(out, exist_ok=True)
    exe = os.path.join(project, "Builds", "E4", "E4.exe")
    started = time.time()
    code = subprocess.call([exe, "-e4out", out, "-logFile", os.path.join(out, "player.log")], timeout=1800)
    print("%s bench: exit %d in %.0f s" % (pipeline, code, time.time() - started))
    path = os.path.join(out, "results.json")
    if os.path.exists(path):
        data = json.load(open(path, encoding="utf-8"))
        print("    %s, %s, %dx%d" % (data["device"], data["api"], data["width"], data["height"]))
        for r in data["results"]:
            print("    %-20s %-14s %-8s frame %6.2f ms (p95 %6.2f)  CPU main %5.2f  render thread %5.2f  GPU %5.2f" % (
                r["pipeline"], r["quality"], r["shot"], r["frameMs"], r["frameP95Ms"], r["cpuMs"], r["renderThreadMs"],
                r["gpuMs"]))


def main(args):
    steps = [a for a in args if a in ("setup", "bench")] or ["setup", "bench"]
    pipelines = [a for a in args if a in PROJECTS] or list(PROJECTS)
    if "setup" in steps:
        started = time.time()
        running = {p: start_setup(p, PROJECTS[p]) for p in pipelines}  # both editors at once
        ok = {p: report_setup(p, proc, log, started) for p, (proc, log) in running.items()}
        pipelines = [p for p in pipelines if ok[p]]
    if "bench" in steps:
        for p in pipelines:  # one at a time: they share the GPU
            bench(p, PROJECTS[p])


if __name__ == "__main__":
    main(sys.argv[1:])
