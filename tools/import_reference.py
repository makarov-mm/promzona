"""One-time, read-only extraction from the managed repository. Not used by builds."""
from pathlib import Path
import argparse, hashlib, json, shutil, xml.etree.ElementTree as ET

parser = argparse.ArgumentParser()
parser.add_argument('repository', type=Path)
args = parser.parse_args()
repo = args.repository.resolve()
root = Path(__file__).resolve().parents[1]
reference = root / 'migration' / 'reference'
if reference.exists():
    raise SystemExit('Reference already exists; refusing to overwrite a migration baseline')
records = []
destinations = {}
def copy(src, dst, category):
    src = src.resolve()
    if not src.is_relative_to(repo) or not src.is_file():
        raise ValueError(f'Missing or external dependency: {src}')
    key = dst.relative_to(root).as_posix()
    if key in destinations:
        if destinations[key] != src:
            raise ValueError(f'Conflicting sources for {key}')
        return
    destinations[key] = src
    dst.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(src, dst)
    data = dst.read_bytes()
    records.append(dict(source=src.relative_to(repo).as_posix(), path=dst.relative_to(root).as_posix(), category=category, size=len(data), sha256=hashlib.sha256(data).hexdigest()))

projects, queue = set(), [repo/'Apps/Promzona/Promzona.csproj', repo/'Apps/Promzona.Tests/Promzona.Tests.csproj']
while queue:
    project = queue.pop().resolve()
    if project in projects: continue
    projects.add(project)
    xml = ET.parse(project)
    queue.extend(project.parent / item.attrib['Include'].replace('\\','/') for item in xml.findall('.//ProjectReference'))
    for src in project.parent.rglob('*'):
        rel = src.relative_to(project.parent)
        if any(p in ('bin','obj','Content','.git') for p in rel.parts): continue
        if src.is_file() and src.suffix.lower() in ('.cs','.fs','.csproj','.fsproj','.props','.targets','.json','.vert','.frag','.glsl','.txt','.md','.manifest','.slnx'):
            copy(src, reference / src.relative_to(repo), 'reference')
    for item in xml.findall('.//Compile'):
        inc = item.attrib.get('Include', '').replace('\\','/')
        if inc and '*' not in inc and ';' not in inc and '..' in inc:
            src = (project.parent / inc).resolve()
            dst = reference / src.relative_to(repo)
            if not dst.exists(): copy(src, dst, 'linked-source')

game = repo/'Apps/Promzona'
for item in ET.parse(game/'RuntimeContent.props').findall('.//Content'):
    inc = item.attrib['Include'].replace('\\','/')
    dest = item.attrib.get('Link',inc.removeprefix('Content/'))
    if '*' in inc or ';' in inc or '$(' in inc: raise ValueError(inc)
    dst = (root/'Content'/dest).resolve()
    if not dst.is_relative_to(root/'Content'): raise ValueError(dest)
    copy(game/inc, dst, 'runtime-asset')
for lang in ('en','ru','uk'):
    copy(repo/f'Languages/Main/{lang}.xml',root/f'Content/Languages/Main/{lang}.xml','language')
copy(game/'Content/PROMZONA.ico',root/'Content/PROMZONA.ico','icon')
copy(repo/'Apps/Makarov.Host/app.manifest',reference/'Apps/Makarov.Host/app.manifest','linked-source')
for f in ('Directory.Build.props','global.json'):
    copy(repo/f,reference/f,'reference')

manifest = dict(version=1, source_version=ET.parse(game/'Promzona.csproj').findtext('.//Version'), projects=sorted(p.relative_to(repo).as_posix() for p in projects), files=records)
(root/'migration/import-manifest.json').write_text(json.dumps(manifest,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(dict(projects=len(projects),files=len(records),bytes=sum(r['size'] for r in records),code_files=sum(r['path'].endswith(('.cs','.fs')) for r in records)),indent=2))
