import { useState, useEffect } from 'react';

const BASE = 'https://n8jlr9dr-5000.euw.devtunnels.ms';

export default function AvailableHoursPanel() {
  const [tab, setTab] = useState('summary');
  const [data, setData] = useState([]);
  const [loading, setLoading] = useState(false);
  const [teamLead, setTeamLead] = useState('');
  const [teamLeads, setTeamLeads] = useState([]);
  const today = new Date().toISOString().split('T')[0];
  const [from, setFrom] = useState(today);
  const [to, setTo] = useState(today);

  const fetchData = async () => {
    setLoading(true);
    try {
      const params = new URLSearchParams({ from, to });
      if (teamLead) params.append('teamLead', teamLead);
      const res = await fetch(`${BASE}/api/wic/availableHours?${params}`);
      const json = await res.json();
      setData(json);
      const tls = [...new Set(json.map(r => r.teamLead).filter(Boolean))];
      setTeamLeads(tls);
    } catch(e) { console.error(e); }
    setLoading(false);
  };

  useEffect(() => { fetchData(); }, [from, to, teamLead]);

  const handleDownload = () => {
    const params = new URLSearchParams({ from, to });
    if (teamLead) params.append('teamLead', teamLead);
    window.open(`${BASE}/api/wic/availableHours/download?${params}`);
  };

  const summaryByDate = data.reduce((acc, row) => {
    if (!acc[row.date]) acc[row.date] = { agents: 0, totalFree: 0 };
    acc[row.date].agents++;
    acc[row.date].totalFree += row.freeHours || 0;
    return acc;
  }, {});

  const cellStyle = { padding: '8px 12px', fontSize: 12, borderBottom: '1px solid rgba(255,255,255,0.05)', color: '#e2e8f0' };
  const headStyle = { padding: '8px 12px', fontSize: 11, color: '#8892a4', borderBottom: '1px solid rgba(255,255,255,0.1)', textTransform: 'uppercase', fontWeight: 600, letterSpacing: '0.05em' };

  return (
    <div style={{ background: '#181e2e', border: '1px solid rgba(255,255,255,0.07)', borderRadius: 10, marginTop: 24, padding: 20 }}>
      <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom: 16 }}>
        <span style={{ fontSize: 12, fontWeight: 700, color: '#8892a4', letterSpacing: '0.08em', textTransform: 'uppercase' }}>Available Hours for Backlog / Voice</span>
        <button onClick={handleDownload} style={{ background: 'rgba(96,165,250,0.1)', border: '1px solid #60a5fa', color: '#60a5fa', borderRadius: 6, padding: '4px 12px', fontSize: 11, cursor: 'pointer', fontWeight: 600 }}>CSV</button>
      </div>
      <div style={{ display:'flex', gap: 10, marginBottom: 16, flexWrap:'wrap' }}>
        <input type="date" value={from} onChange={e => setFrom(e.target.value)}
          style={{ background:'#0f1117', border:'1px solid rgba(255,255,255,0.1)', color:'#e2e8f0', borderRadius:6, padding:'4px 8px', fontSize:12 }} />
        <input type="date" value={to} onChange={e => setTo(e.target.value)}
          style={{ background:'#0f1117', border:'1px solid rgba(255,255,255,0.1)', color:'#e2e8f0', borderRadius:6, padding:'4px 8px', fontSize:12 }} />
        <select value={teamLead} onChange={e => setTeamLead(e.target.value)}
          style={{ background:'#0f1117', border:'1px solid rgba(255,255,255,0.1)', color: teamLead ? '#e2e8f0' : '#8892a4', borderRadius:6, padding:'4px 8px', fontSize:12 }}>
          <option value=''>All Team Leads</option>
          {teamLeads.map(tl => <option key={tl} value={tl}>{tl}</option>)}
        </select>
      </div>
      <div style={{ display:'flex', gap:4, marginBottom:16 }}>
        {['summary','detail'].map(t => (
          <button key={t} onClick={() => setTab(t)} style={{ background: tab===t ? 'rgba(96,165,250,0.15)' : 'transparent', border: tab===t ? '1px solid #60a5fa' : '1px solid transparent', color: tab===t ? '#60a5fa' : '#8892a4', borderRadius: 6, padding: '4px 14px', fontSize: 11, cursor: 'pointer', fontWeight: 600, textTransform: 'uppercase', letterSpacing: '0.05em' }}>
            {t}
          </button>
        ))}
      </div>
      {loading && <div style={{ color:'#8892a4', fontSize:12, padding:'20px 0', textAlign:'center' }}>Loading...</div>}
      {!loading && tab === 'summary' && (
        <table style={{ width:'100%', borderCollapse:'collapse' }}>
          <thead><tr>{['Date','WIC Agents','Total Free Hours'].map(h => <th key={h} style={headStyle}>{h}</th>)}</tr></thead>
          <tbody>
            {Object.entries(summaryByDate).map(([date, s]) => (
              <tr key={date}>
                <td style={cellStyle}>{date}</td>
                <td style={cellStyle}>{s.agents}</td>
                <td style={{...cellStyle, color:'#22c55e', fontWeight:600}}>{s.totalFree.toFixed(1)}h</td>
              </tr>
            ))}
            {Object.keys(summaryByDate).length === 0 && <tr><td colSpan={3} style={{...cellStyle, textAlign:'center', color:'#8892a4'}}>No data</td></tr>}
          </tbody>
        </table>
      )}
      {!loading && tab === 'detail' && (
        <table style={{ width:'100%', borderCollapse:'collapse' }}>
          <thead><tr>{['Date','Agent','Team Lead','Location','WIC Hours','Agent Shift','Free Hours'].map(h => <th key={h} style={headStyle}>{h}</th>)}</tr></thead>
          <tbody>
            {data.map((row, i) => {
              const fh = row.freeHours || 0;
              const barColor = fh > 4 ? '#22c55e' : fh >= 2 ? '#f97316' : '#ef4444';
              return (
                <tr key={i}>
                  <td style={cellStyle}>{row.date}</td>
                  <td style={{...cellStyle, fontWeight:500}}>{row.name}</td>
                  <td style={{...cellStyle, color:'#8892a4'}}>{row.teamLead}</td>
                  <td style={cellStyle}>{row.location}</td>
                  <td style={cellStyle}>{row.wicOpenTime}-{row.wicCloseTime}</td>
                  <td style={{...cellStyle, color:'#60a5fa'}}>{row.agentStartTime}-{row.agentEndTime}</td>
                  <td style={cellStyle}>
                    <div style={{ display:'flex', alignItems:'center', gap:8 }}>
                      <span style={{ color: barColor, fontWeight:600, minWidth:32 }}>{fh.toFixed(1)}h</span>
                      <div style={{ flex:1, height:4, background:'rgba(255,255,255,0.07)', borderRadius:2, minWidth:60 }}>
                        <div style={{ width: Math.min(100,(fh/8)*100)+'%', height:'100%', background: barColor, borderRadius:2 }} />
                      </div>
                    </div>
                  </td>
                </tr>
              );
            })}
            {data.length === 0 && <tr><td colSpan={7} style={{...cellStyle, textAlign:'center', color:'#8892a4'}}>No data</td></tr>}
          </tbody>
        </table>
      )}
    </div>
  );
}
