import { useState, useEffect } from 'react'

const BASE = ''
const COLORS = {
  voice:    '#22c55e',
  wic:      '#60a5fa',
  al:       '#f97316',
  sick:     '#ef4444',
  training: '#c084fc',
  off:      '#8892a4',
}

export default function CoverageBar({ date }) {
  const [data, setData] = useState(null)
  const [tooltip, setTooltip] = useState(null)

  useEffect(() => {
    if (!date) return
    fetch(`${BASE}/api/shifts/coverage?date=${date}`)
      .then(r => r.json())
      .then(setData)
      .catch(console.error)
  }, [date])

  if (!data) return null

  const maxAgents = Math.max(...data.slots.map(s => s.voice + s.wic + s.al + s.sick + s.training + s.off), 1)

  return (
    <div style={{ background:'#181e2e', border:'1px solid rgba(255,255,255,0.07)', borderRadius:10, padding:'14px 16px', marginBottom:16 }}>
      <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:10 }}>
        <span style={{ fontSize:11, fontWeight:700, color:'#8892a4', letterSpacing:'0.08em', textTransform:'uppercase' }}>Coverage per Hour</span>
        <div style={{ display:'flex', gap:12 }}>
          {Object.entries(COLORS).map(([k,c]) => (
            <span key={k} style={{ display:'flex', alignItems:'center', gap:4, fontSize:10, color:'#8892a4' }}>
              <span style={{ width:8, height:8, borderRadius:2, background:c, display:'inline-block' }}/>
              {k.charAt(0).toUpperCase()+k.slice(1)}
            </span>
          ))}
        </div>
      </div>
      <div style={{ display:'flex', gap:4, alignItems:'flex-end', height:60 }}>
        {data.slots.map(slot => {
          const pct = h => `${(h / maxAgents) * 100}%`
          return (
            <div key={slot.hour} style={{ flex:1, display:'flex', flexDirection:'column', alignItems:'center', gap:2, cursor:'pointer' }}
              onMouseEnter={() => setTooltip(slot)}
              onMouseLeave={() => setTooltip(null)}>
              <div style={{ width:'100%', height:48, display:'flex', flexDirection:'column', justifyContent:'flex-end',
                border: slot.belowThreshold ? '1px solid rgba(239,68,68,0.4)' : '1px solid transparent',
                borderRadius:4, overflow:'hidden', background:'rgba(255,255,255,0.03)' }}>
                {[['off',slot.off],['training',slot.training],['sick',slot.sick],['al',slot.al],['wic',slot.wic],['voice',slot.voice]].map(([key,val]) =>
                  val > 0 ? <div key={key} style={{ width:'100%', height:pct(val), background:COLORS[key], opacity:0.85 }}/> : null
                )}
              </div>
              <span style={{ fontSize:9, color:'#8892a4', whiteSpace:'nowrap' }}>{slot.hour}</span>
            </div>
          )
        })}
      </div>
      {tooltip && (
        <div style={{ marginTop:8, padding:'8px 12px', background:'rgba(255,255,255,0.05)', borderRadius:6, fontSize:11, color:'#e2e8f0' }}>
          <strong>{tooltip.hour}</strong> — {tooltip.voice + tooltip.wic} available
          {tooltip.belowThreshold && <span style={{ color:'#ef4444', marginLeft:8 }}>⚠ Below minimum ({data.threshold})</span>}
          <span style={{ marginLeft:12, color:'#22c55e' }}>Voice: {tooltip.voice}</span>
          <span style={{ marginLeft:8, color:'#60a5fa' }}>WIC: {tooltip.wic}</span>
          {tooltip.al > 0 && <span style={{ marginLeft:8, color:'#f97316' }}>AL: {tooltip.al}</span>}
          {tooltip.sick > 0 && <span style={{ marginLeft:8, color:'#ef4444' }}>Sick: {tooltip.sick}</span>}
          {tooltip.training > 0 && <span style={{ marginLeft:8, color:'#c084fc' }}>Training: {tooltip.training}</span>}
        </div>
      )}
      {data.slots.some(s => s.belowThreshold) && (
        <div style={{ marginTop:8, padding:'6px 10px', background:'rgba(239,68,68,0.08)', border:'1px solid rgba(239,68,68,0.2)', borderRadius:6, fontSize:11, color:'#ef4444' }}>
          ⚠ Coverage below minimum ({data.threshold}) at: {data.slots.filter(s => s.belowThreshold).map(s => s.hour).join(', ')}
        </div>
      )}
    </div>
  )
}

