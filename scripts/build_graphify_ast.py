import json
from pathlib import Path

import graphify.analyze as analyze
from graphify.analyze import god_nodes, surprising_connections, suggest_questions
from graphify.build import build_from_json
from graphify.cluster import cluster, score_all
from graphify.export import to_json
from graphify.extract import collect_files, extract
from graphify.report import generate


ROOT = Path(__file__).resolve().parents[1]
OUT = ROOT / "graphify-out"
OUT.mkdir(exist_ok=True)

files = [p for p in collect_files(ROOT, root=ROOT) if p.suffix.lower() in analyze.CODE_EXTENSIONS]
result = extract(files, root=ROOT, parallel=False) if files else {"nodes": [], "edges": []}
(OUT / "ast-extraction.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
graph = build_from_json(result, root=ROOT)
communities = cluster(graph)
cohesion = score_all(graph, communities)
labels = {cid: f"Community {cid}" for cid in communities}
to_json(graph, communities, str(OUT / "graph.json"), force=True, community_labels=labels)
detection = {"total_files": len(files), "total_words": 0, "files": {"code": [str(p.relative_to(ROOT)) for p in files]}}
report = generate(graph, communities, cohesion, labels, god_nodes(graph), surprising_connections(graph, communities), detection, {"input": 0, "output": 0}, str(ROOT), suggest_questions(graph, communities, labels))
(OUT / "GRAPH_REPORT.md").write_text(report, encoding="utf-8")
print(f"AST: {len(result.get('nodes', []))} nodes, {len(result.get('edges', []))} edges")
print(f"Graph: {graph.number_of_nodes()} nodes, {graph.number_of_edges()} edges, {len(communities)} communities")
