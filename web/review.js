function compareScans(before, after) {
  const groups = new Map();
  const key = f => JSON.stringify([f.project || '', f.path, f.category, f.title, f.subject || '', f.excerpt.trim()]);
  const previousCoverage = new Map((before.coverage || []).map(c => [c.path, c.status]));
  const coverage = new Map((after.coverage || []).map(c => [c.path, c.status]));
  const current = after.findings.map(f => ({finding:f, used:false}));
  for (const item of current) {
    const k = key(item.finding);
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(item);
  }
  const rows = [];
  const unmatched = [];
  function match(old, exact) {
    const candidates = (groups.get(key(old)) || []).filter(x => !x.used);
    const found = exact ? candidates.find(x => x.finding.context === old.context) : candidates.length === 1 ? candidates[0] : null;
    if (!found) return false;
    found.used = true;
    rows.push({status:'Still present', finding:found.finding, previous:old});
    return true;
  }
  for (const old of before.findings) if (!match(old,true)) unmatched.push(old);
  for (const old of unmatched) {
    if (match(old,false)) continue;
    const state = coverage.get(old.path);
    const missingPeer = old.title.startsWith('Multiple ') && before.findings.some(f =>
      f.title === old.title && f.project === old.project && coverage.get(f.path) !== 'Analyzed' &&
      !after.findings.some(n => n.path === f.path));
    const resolved = state === 'Analyzed' && !missingPeer;
    rows.push({status:resolved ? 'Resolved' : 'Unverified', finding:old,
      reason:resolved ? 'The file was checked and the previous finding is absent.' :
        missingPeer ? 'A related dependency file could not be fully checked.' : 'Current file coverage: '+(state || 'absent from ZIP')+'.'});
  }
  for (const item of current) if (!item.used) {
    const past = previousCoverage.get(item.finding.path);
    const incomplete = past && past !== 'Analyzed' && past !== coverage.get(item.finding.path);
    rows.push({status:incomplete ? 'Unverified' : 'New',finding:item.finding,
      reason:incomplete ? 'The baseline had incomplete coverage for this file.' : undefined});
  }
  return rows;
}

function CoverageView({files}) {
  const [filter, set] = React.useState('All');
  const totals = ['Analyzed','Limited','Skipped','Unreadable'];
  const frontendFiles = files.filter(f=>['Analyzed','Limited'].includes(f.status) &&
    /(?:\.(?:[cm]?js|jsx|ts|tsx|css|html?|cshtml|razor|aspx|ascx|master)|(?:package(?:-lock)?|libman|bower)\.json)$/i.test(f.path)).length;
  const visible = files.filter(f => filter==='All' || filter===f.status);
  return html`<section className="report-panel"><h2>Scan coverage</h2><p>${frontendFiles} frontend files checked, including asset metadata.</p><div className="pills">${['All',...totals].map(s=>html`<button key=${s} aria-pressed=${filter===s} onClick=${()=>set(s)}>${s} · ${s==='All'?files.length:files.filter(f=>f.status===s).length}</button>`)}</div>
    <div className="report-scroll">${visible.map(f=>html`<div className="report-line" key=${f.path}><b>${f.path}</b><span>${f.status} · ${f.reason}</span></div>`)}${!visible.length && html`<p>No files in this group.</p>`}</div></section>`;
}

function UsageView({map}) {
  const [query, set] = React.useState('');
  const edges=map.edges.filter(e=>[e.from,e.to,e.relation].join(' ').toLowerCase().includes(query.toLowerCase()));
  return html`<section className="report-panel"><h2>Dependency usage</h2><p>Select a source or target to follow its references.</p>
    <input aria-label="Search dependency usage" placeholder="Find a view, bundle or package" value=${query} onInput=${e=>set(e.target.value)}/>
    ${query && html`<button onClick=${()=>set('')}>Show all</button>`}${map.truncated && html`<p>The dependency map reached its 2,000-reference limit.</p>`}
    <div className="report-scroll">${edges.map((e,i)=>html`<div key=${i} className="report-line"><button className="path-btn" onClick=${()=>set(e.from)}>${e.from}</button>
      <span>${e.line?'Line '+e.line+' · ':''}${e.relation} · ${e.confidence}</span><button className="path-btn" onClick=${()=>set(e.to)}>${e.to}</button></div>`)}
    ${!edges.length && html`<p>No matching dependency references.</p>`}</div></section>`;
}

function ChangesView({comparison, inspect}) {
  const [filter,set]=React.useState('All');
  if (!comparison) return html`<section className="report-panel"><h2>Compare scans</h2><p>Choose <b>Compare revised ZIP</b> and upload the same project with the same folder structure.</p></section>`;
  const rows=comparison.rows;
  return html`<section className="report-panel"><h2>Scan changes</h2><p>${comparison.before} → ${comparison.after}</p>
    <div className="pills">${['All','New','Still present','Resolved','Unverified'].map(s=>html`<button key=${s} aria-pressed=${s===filter} onClick=${()=>set(s)}>${s} · ${s==='All'? rows.length:rows.filter(r=>r.status===s).length}</button>`)}</div>
    <div className="report-scroll">${rows.filter(r=>filter==='All'||r.status===filter).map((r,i)=>html`<div className="report-line" key=${i}><b>${r.status} · ${r.finding.title}</b>
    <span>${r.finding.path} · L${r.finding.line}</span>${r.reason && html`<span>${r.reason}</span>`}${['New','Still present'].includes(r.status) && html`<button className="path-btn" onClick=${()=>inspect(r.finding)}>Open finding</button>`}</div>`)}</div></section>`;
}
