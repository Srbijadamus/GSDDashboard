import { useState, useEffect } from 'react'

export default function LeaveAvailabilityBar({ from, to, maxLeave = 8 }) {
  const [data, setData] = useState([])
  const [loading, setLoading] = useState(false)
  const [max, setMax] = useState(maxLeave)
  const [tooltip, setTooltip] = useState(null)

  useEffect(() => {
    if (!from || !to) return
    setLoading(true)
    fetch(`/api/vacations/availability?from=${from}&to=${to}&maxLeave=${max}`)
      .then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() })
      .then(d => { setData(Array.isArray(d) ? d : []); setLoading(false) })
      .catch(() => setLoading(false))
  }, [from, to, max])

  const barColor = d => {
    const pct = d.totalOff / d.maxLeave
    if (d.isFull) return '#ef4444'
    if (pct > 0.8) return '#f97316'
    return '#22c55e'
  }

  return (
    <div style={{ background:'#181e2e', border:'1px solid rgba(255,255,255,0.07)', borderRadius:10, padding:'14px 16px', marginBottom:16 }}>
      <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:12 }}>
        <span style={{ fontSize:11, fontWeight:700, color:'#8892a4', letterSpacing:'0.08em', textTransform:'uppercase' }}>Leave Availability</span>
        <div style={{ display:'flex', alignItems:'center', gap:8 }}>
          <span style={{ fontSize:11, color:'#8892a4' }}>Max per day:</span>
          <input type="number" min={1} max={30} value={max} onChange={e => setMax(Number(e.target.value))}
            style={{ width:48, background:'#0f1117', border:'1px solid rgba(255,255,255,0.1)', color:'#e2e8f0', borderRadius:6, padding:'2px 6px', fontSize:12, textAlign:'center' }} />
        </div>
      </div>
      {loading && <div style={{ color:'#8892a4', fontSize:12, textAlign:'center', padding:'12px 0' }}>Loading...</div>}
      {!loading && (
        <>
          <div style={{ display:'flex', gap:3, alignItems:'flex-end', flexWrap:'wrap' }}>
            {data.map(d => {
              const pct = Math.min(1, d.totalOff / d.maxLeave)
              const color = barColor(d)
              const dateObj = new Date(d.date)
              const label = dateObj.toLocaleDateString('en', { weekday:'short', day:'2-digit', month:'2-digit' })
              return (
                <div key={d.date} style={{ display:'flex', flexDirection:'column', alignItems:'center', gap:3, cursor:'pointer', minWidth:44 }}
                  onMouseEnter={() => setTooltip(d)} onMouseLeave={() => setTooltip(null)}>
                  <span style={{ fontSize:9, color: d.isFull ? '#ef4444' : '#8892a4', fontWeight: d.isFull ? 700 : 400 }}>{d.totalOff}/{d.maxLeave}</span>
                  <div style={{ width:36, height:40, background:'rgba(255,255,255,0.03)', border: d.isFull ? '1px solid rgba(239,68,68,0.4)' : '1px solid rgba(255,255,255,0.07)', borderRadius:4, display:'flex', flexDirection:'column', justifyContent:'flex-end', overflow:'hidden' }}>
                    <div style={{ width:'100%', height:`${pct*100}%`, background:color, opacity:0.8, transition:'height 0.2s' }}/>
                  </div>
                  <span style={{ fontSize:9, color:'#8892a4', textAlign:'center', whiteSpace:'nowrap' }}>{label}</span>
                </div>
              )
            })}
          </div>
          {tooltip && (
            <div style={{ marginTop:10, padding:'8px 12px', background:'rgba(255,255,255,0.05)', borderRadius:6, fontSize:11, color:'#e2e8f0' }}>
              <strong>{tooltip.date}</strong> — <span style={{ color:barColor(tooltip) }}>{tooltip.totalOff} off</span> of max <strong>{tooltip.maxLeave}</strong> — <span style={{ color:'#22c55e' }}>{tooltip.remaining} slots remaining</span>
              {tooltip.alCount > 0 && <span style={{ marginLeft:10, color:'#f97316' }}>AL: {tooltip.alCount}</span>}
              {tooltip.slCount > 0 && <span style={{ marginLeft:8, color:'#ef4444' }}>SL: {tooltip.slCount}</span>}
              {tooltip.isFull && <span style={{ marginLeft:10, color:'#ef4444', fontWeight:700 }}>⛔ FULL</span>}
            </div>
          )}
          {data.some(d => d.isFull) && (
            <div style={{ marginTop:8, padding:'6px 10px', background:'rgba(239,68,68,0.08)', border:'1px solid rgba(239,68,68,0.2)', borderRadius:6, fontSize:11, color:'#ef4444' }}>
              ⛔ Leave limit reached on: {data.filter(d => d.isFull).map(d => d.date).join(', ')}
            </div>
          )}
        </>
      )}
    </div>
  )
}
