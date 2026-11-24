const html = htm.bind(React.createElement);
const {useState, useRef, useEffect} = React;
async function api(path, options) {
  const response = await fetch(path, options);
  if (!response.headers.get('content-type')?.includes('json')) throw Error('Live scans need the CoreWaypoint .NET server. The sample report works offline.');
  const data = await response.json();
  if (!response.ok) throw Error(typeof data === 'string' ? data : data.detail || 'Request failed. Please retry.');
  return data;
}
function App() {
  const [scan, setScan] = useState(example);
  const [id, setId] = useState(null);
  const [plan, setPlan] = useState(examplePlan);
  const [tab, setTab] = useState('Findings');
  const [selected, select] = useState(example.findings[0]);
  const [filter, setFilter] = useState('All categories');
  const [search, setSearch] = useState('');
  const [busy, setBusy] = useState('');
  const [error, setError] = useState('');
  const [provider, setProvider] = useState(null);
  const input = useRef(null);
  useEffect(()=>{api('/api/provider').then(setProvider).catch(()=>{});},[]);
  const findings = scan.findings.filter(f=>(filter==='All categories' || f.category===filter) &&
    (f.path+' '+f.title+' '+f.id).toLowerCase().includes(search.toLowerCase()));
  function inspect(f) {
    select(f); setSearch(''); setFilter('All categories'); setTab('Findings');
    requestAnimationFrame(()=>document.getElementById('evidence')?.scrollIntoView({block:'nearest'}));
  }
  async function upload(event) {
    const file=event.target.files[0]; event.target.value='';
    if(!file) return;
    setError('');
    if (!file.name.toLowerCase().endsWith('.zip') || file.size>10000000){setError('Select a ZIP archive up to 10 MB.');return;}
    setBusy('Scanning source…');
    try {
      const form=new FormData(); form.append('file',file);
      const result=await api('/api/scans',{method:'POST',body:form});
      setScan(result.scan);setId(result.id);setPlan(null);select(result.scan.findings[0]);setTab('Findings');setFilter('All categories');setSearch('');
    } catch(e){setError(e.message);} finally{setBusy('');}
  }
  async function generate(){
    setBusy('Generating plan…');setError('');
    try{setPlan(await api('/api/scans/'+id+'/plan',{method:'POST'}));}catch(e){setError(e.message);}finally{setBusy('');}
  }
  function exportPlan(){
    const blob=new Blob([JSON.stringify({project:scan.name,findings:scan.findings,plan},null,2)],{type:'application/json'});
    const url=URL.createObjectURL(blob), a=document.createElement('a');a.href=url;a.download=scan.name+'-migration.json';a.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
  }
  return html`<header><div className="brand"><i className="mark" aria-hidden="true"></i>CoreWaypoint</div><span className="headerline"></span><span>Migration workspace</span><div className="provider">${provider?provider.provider:'Sample workspace'}</div></header>
  <main><section className="hero"><div><p className="eyebrow">ASP.NET MODERNIZATION</p><h1>Migration workspace<span>.</span></h1><p>Explore legacy dependencies and build an evidence-backed migration plan.</p><div className="actions"><button className="primary" disabled=${!!busy} onClick=${()=>input.current.click()}>＋ Upload project</button><a href="storefront.zip" download>Download sample ZIP</a></div><input hidden type="file" accept=".zip" ref=${input} onChange=${upload}/></div><div className="art" aria-hidden="true"><i></i><i></i><i></i><b>.NET</b></div></section>
  ${(busy || error) && html`<div role="status" className="notice">${busy || error}</div>`}
  <section className="summary"><div><p className="eyebrow">${id?'PROJECT':'SAMPLE PROJECT'}</p><h2>${scan.name}</h2></div><div><span>Source files</span><strong>${scan.files}</strong></div><div><span>Findings</span><strong>${scan.findings.length}</strong></div><div><span>Migration target</span><strong>.NET 10</strong></div></section>
  <nav aria-label="Report views">${['Findings','Migration plan'].map(t=>html`<button key=${t} aria-pressed=${t===tab} onClick=${()=>setTab(t)}>${t}</button>`)}<button className="export" onClick=${exportPlan}>Export report ↓</button></nav>
  ${tab==='Findings'?html`<section className="split"><article><div className="heading"><h2>Source findings</h2><span className="number">${findings.length}</span></div><div className="filters"><input aria-label="Search findings" placeholder="Search findings or files" value=${search} onInput=${e=>setSearch(e.target.value)}/><select aria-label="Filter by category" value=${filter} onChange=${e=>setFilter(e.target.value)}>${['All categories',...new Set(scan.findings.map(f=>f.category))].map(c=>html`<option key=${c}>${c}</option>`)}</select></div><div className="list">${findings.map(f=>html`<button className=${'row '+(selected?.id===f.id?'current':'')} key=${f.id} onClick=${()=>select(f)}><span className="glyph" aria-hidden="true">${f.category==='Configuration'?'⚙':'{ }'}</span><span><b>${f.title}</b><small>${f.path} · L${f.line}</small></span><em>${f.category}</em></button>`)}${!findings.length && html`<p>No matching findings.</p>`}</div></article>
  <aside id="evidence"><p className="eyebrow">SOURCE EVIDENCE</p>${selected?html`<h2>${selected.title}</h2><p className="file">${selected.path}</p><div className="code"><span>LINE ${selected.line}</span><pre>${selected.excerpt}</pre></div><div className="meta"><span>Rule category</span><b>${selected.category}</b></div><div className="meta"><span>Evidence ID</span><code>${selected.id}</code></div>`:html`<p>No source evidence available.</p>`}</aside></section>`
  :html`<section className="plan"><div className="heading"><h2>${id?'Migration plan':'Example migration plan'}</h2></div>${plan?plan.phases.map((p,i)=>html`<article key=${i}><p className="eyebrow">PHASE ${i+1}</p><h3>${p.title}</h3><p>${p.action}</p><p><b>Verify</b> · ${p.test}</p>${p.evidence.map(e=>html`<button className="chip" key=${e} onClick=${()=>inspect(scan.findings.find(f=>f.id===e))}>${e}</button>`)}</article>`):html`<p>Generate ordered migration steps and verification checks from your scan.</p><p className="file">${provider?.provider==='Groq'?'Groq receives finding IDs, file paths, rule names and line numbers.':'Generation uses the AI provider configured on the server.'}</p><button className="primary" disabled=${!!busy || !scan.findings.length} onClick=${generate}>Generate migration plan</button>`}${plan && !plan.phases.length && html`<p>No migration steps for this scan.</p>`}</section>`}
  </main><footer>CoreWaypoint <span>ASP.NET migration workspace</span></footer>`;
}
ReactDOM.createRoot(document.getElementById('root')).render(html`<${App}/>`);
