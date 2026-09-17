import { useState, useEffect } from 'react'
import { useTranslation } from 'react-i18next'

const BASE = ''
// Order matters for the stacked bar (bottom → top). Colors are chosen to not collide with
// existing Task badge colors on the Shift Plan grid (see Shifts.tsx taskStyle()).
const COLORS = {
  voice:    'rgb(var(--st-good-solid))',
  vwic:     'rgb(var(--st-holiday-solid))',
  wic:      'rgb(var(--st-wic-solid))',
  backlog:  'rgb(var(--st-warn-solid))',
  other:    'rgb(var(--st-muted-solid))',
  al:       'rgb(var(--st-info-solid))',
  sick:     'rgb(var(--st-crit-solid))',
  training: 'rgb(var(--st-learn-solid))',
  off:      'rgb(var(--st-neutral-solid))',
}

export default function CoverageBar({ date }) {
  const { t } = useTranslation()
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

  const maxAgents = Math.max(...data.slots.map(s => s.voice + s.vwic + s.wic + s.backlog + s.other + s.al + s.sick + s.training + s.off), 1)

  return (
    <div className="bg-raised border border-line-subtle" style={{ borderRadius:10, padding:'14px 16px', marginBottom:16 }}>
      <div style={{ display:'flex', justifyContent:'space-between', alignItems:'center', marginBottom:10 }}>
        <span className="text-ink-soft" style={{ fontSize:11, fontWeight:700, letterSpacing:'0.08em', textTransform:'uppercase' }}>{t('shifts.coveragePerHour')}</span>
        <div style={{ display:'flex', gap:12, flexWrap:'wrap' }}>
          {Object.keys(COLORS).map(k => (
            <span key={k} className="text-ink-soft" style={{ display:'flex', alignItems:'center', gap:4, fontSize:10 }}>
              <span style={{ width:8, height:8, borderRadius:2, background:COLORS[k], display:'inline-block' }}/>
              {t(`shifts.coverageLegend.${k}`)}
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
                {[['off',slot.off],['training',slot.training],['sick',slot.sick],['al',slot.al],['other',slot.other],['backlog',slot.backlog],['wic',slot.wic],['vwic',slot.vwic],['voice',slot.voice]].map(([key,val]) =>
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
          <strong>{tooltip.hour}</strong> — {tooltip.voice} {t('shifts.coverageLegend.voice').toLowerCase()}
          {tooltip.belowThreshold && <span className="text-crit-fg" style={{ marginLeft:8 }}>⚠ {t('shifts.belowMinimum', { min: tooltip.minRequired })}</span>}
          <span className="text-good-fg" style={{ marginLeft:12 }}>{t('shifts.coverageLegend.voice')}: {tooltip.voice}</span>
          <span className="text-holiday-fg" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.vwic')}: {tooltip.vwic}</span>
          <span className="text-wic-fg" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.wic')}: {tooltip.wic}</span>
          {tooltip.backlog > 0 && <span className="text-warn-fg" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.backlog')}: {tooltip.backlog}</span>}
          {tooltip.other > 0 && <span className="text-ink-soft" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.other')}: {tooltip.other}</span>}
          {tooltip.al > 0 && <span className="text-info-fg" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.al')}: {tooltip.al}</span>}
          {tooltip.sick > 0 && <span className="text-crit-fg" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.sick')}: {tooltip.sick}</span>}
          {tooltip.training > 0 && <span className="text-learn-fg" style={{ marginLeft:8 }}>{t('shifts.coverageLegend.training')}: {tooltip.training}</span>}
        </div>
      )}
      {data.slots.some(s => s.belowThreshold) && (
        <div className="bg-crit-bg border border-crit-bd text-crit-fg" style={{ marginTop:8, padding:'6px 10px', borderRadius:6, fontSize:11 }}>
          ⚠ {t('shifts.belowMinimumAt', { list: data.slots.filter(s => s.belowThreshold).map(s => `${s.hour} (min ${s.minRequired})`).join(', ') })}
        </div>
      )}
    </div>
  )
}

