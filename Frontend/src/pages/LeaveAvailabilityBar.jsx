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
    if (d.isFull) return 'rgb(var(--st-crit-solid))'
    if (pct > 0.8) return 'rgb(var(--st-warn-solid))'
    return 'rgb(var(--st-good-solid))'
  }

  return (
    <div className="bg-raised border border-line-subtle" style={{ borderRadius:10, padding:'14px 16px', marginBottom:16 }}>
      <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:12 }}>
        <span className="text-ink-soft" style={{ fontSize:11, fontWeight:700, letterSpacing:'0.08em', textTransform:'uppercase' }}>Leave Availability</span>
        <div style={{ display:'flex', alignItems:'center', gap:8 }}>
          <span className="text-ink-soft" style={{ fontSize:11 }}>Max per day:</span>
          <input type="number" min={1} max={30} value={max} onChange={e => setMax(Number(e.target.value))}
            className="bg-sunken border border-line-subtle text-ink"
            style={{ width:48, borderRadius:6, padding:'2px 6px', fontSize:12, textAlign:'center' }} />
        </div>
      </div>
      {loading && <div className="text-ink-soft" style={{ fontSize:12, textAlign:'center', padding:'12px 0' }}>Loading...</div>}
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
                  <span style={{ fontSize:9, color: d.isFull ? 'rgb(var(--st-crit-fg))' : 'rgb(var(--text-secondary))', fontWeight: d.isFull ? 700 : 400 }}>{d.totalOff}/{d.maxLeave}</span>
                  <div className="bg-sunken" style={{ width:36, height:40, border: d.isFull ? '1px solid rgb(var(--st-crit-bd))' : '1px solid rgb(var(--line-subtle))', borderRadius:4, display:'flex', flexDirection:'column', justifyContent:'flex-end', overflow:'hidden' }}>
                    <div style={{ width:'100%', height:`${pct*100}%`, background:color, opacity:0.8, transition:'height 0.2s' }}/>
                  </div>
                  <span className="text-ink-soft" style={{ fontSize:9, textAlign:'center', whiteSpace:'nowrap' }}>{label}</span>
                </div>
              )
            })}
          </div>
          {tooltip && (
            <div className="bg-sunken text-ink" style={{ marginTop:10, padding:'8px 12px', borderRadius:6, fontSize:11 }}>
              <strong>{tooltip.date}</strong> — <span style={{ color:barColor(tooltip) }}>{tooltip.totalOff} off</span> of max <strong>{tooltip.maxLeave}</strong> — <span className="text-good-fg">{tooltip.remaining} slots remaining</span>
              {tooltip.alCount > 0 && <span className="text-info-fg" style={{ marginLeft:10 }}>AL: {tooltip.alCount}</span>}
              {tooltip.slCount > 0 && <span className="text-crit-fg" style={{ marginLeft:8 }}>SL: {tooltip.slCount}</span>}
              {tooltip.isFull && <span className="text-crit-fg font-bold" style={{ marginLeft:10 }}>⛔ FULL</span>}
            </div>
          )}
          {data.some(d => d.isFull) && (
            <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ marginTop:8, padding:'6px 10px', borderRadius:6, fontSize:11 }}>
              ⛔ Leave limit reached on: {data.filter(d => d.isFull).map(d => d.date).join(', ')}
            </div>
          )}
        </>
      )}
    </div>
  )
}
