import { useState, useEffect } from 'react'

const BASE = ''
const COLORS = {
  voice:    'rgb(var(--st-good-solid))',
  wic:      'rgb(var(--st-wic-solid))',
  al:       'rgb(var(--st-info-solid))',
  sick:     'rgb(var(--st-crit-solid))',
  training: 'rgb(var(--st-learn-solid))',
  off:      'rgb(var(--st-neutral-solid))',
}

export default function CoverageBar({ date }) {
  const [data, setData] = useState(null)
  const [tooltip, setTooltip] = useState(null)

  useEffect(() => {
    if (!date) return
    fetch(`${BASE}/api/shifts/coverage?date=${date}`)
      .then(r => { if (!r.ok) throw new Error(`API ${r.status}`); return r.json() })
      .then(setData)
      .catch(console.error)
  }, [date])

  if (!data) return null

  const maxAgents = Math.max(...data.slots.map(s => s.voice + s.wic + s.al + s.sick + s.training + s.off), 1)

  return (
    <div className="bg-raised border border-line-subtle" style={{ borderRadius:10, padding:'14px 16px', marginBottom:16 }}>
      <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:10 }}>
        <span className="text-ink-soft" style={{ fontSize:11, fontWeight:700, letterSpacing:'0.08em', textTransform:'uppercase' }}>Coverage per Hour</span>
        <div style={{ display:'flex', gap:12 }}>
          {Object.entries(COLORS).map(([k,c]) => (
            <span key={k} className="text-ink-soft" style={{ display:'flex', alignItems:'center', gap:4, fontSize:10 }}>
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
              <div className="bg-sunken" style={{ width:'100%', height:48, display:'flex', flexDirection:'column', justifyContent:'flex-end',
                border: slot.belowThreshold ? '1px solid rgb(var(--st-crit-bd))' : '1px solid transparent',
                borderRadius:4, overflow:'hidden' }}>
                {[['off',slot.off],['training',slot.training],['sick',slot.sick],['al',slot.al],['wic',slot.wic],['voice',slot.voice]].map(([key,val]) =>
                  val > 0 ? <div key={key} style={{ width:'100%', height:pct(val), background:COLORS[key], opacity:0.85 }}/> : null
                )}
              </div>
              <span className="text-ink-soft" style={{ fontSize:9, whiteSpace:'nowrap' }}>{slot.hour}</span>
            </div>
          )
        })}
      </div>
      {tooltip && (
        <div className="bg-sunken text-ink" style={{ marginTop:8, padding:'8px 12px', borderRadius:6, fontSize:11 }}>
          <strong>{tooltip.hour}</strong> — {tooltip.voice + tooltip.wic} available
          {tooltip.belowThreshold && <span className="text-crit-fg" style={{ marginLeft:8 }}>⚠ Below minimum ({tooltip.minRequired})</span>}
          <span className="text-good-fg" style={{ marginLeft:12 }}>Voice: {tooltip.voice}</span>
          <span className="text-wic-fg" style={{ marginLeft:8 }}>WIC: {tooltip.wic}</span>
          {tooltip.al > 0 && <span className="text-info-fg" style={{ marginLeft:8 }}>AL: {tooltip.al}</span>}
          {tooltip.sick > 0 && <span className="text-crit-fg" style={{ marginLeft:8 }}>Sick: {tooltip.sick}</span>}
          {tooltip.training > 0 && <span className="text-learn-fg" style={{ marginLeft:8 }}>Training: {tooltip.training}</span>}
        </div>
      )}
      {data.slots.some(s => s.belowThreshold) && (
        <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ marginTop:8, padding:'6px 10px', borderRadius:6, fontSize:11 }}>
          ⚠ Coverage below minimum at: {data.slots.filter(s => s.belowThreshold).map(s => `${s.hour} (min ${s.minRequired})`).join(', ')}
        </div>
      )}
    </div>
  )
}
