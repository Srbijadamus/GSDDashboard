import {
  LayoutDashboard, ClipboardList, Building2, Headphones, Coffee,
  Calendar, MapPin, ShieldCheck, Users, Scale, Heart,
  Bot, ListChecks, FileText, CalendarOff,
} from 'lucide-react'
import type { LucideIcon } from 'lucide-react'

export type NavItem = {
  to: string
  icon: LucideIcon
  /** i18n key — all items get this after Step 4 */
  i18nKey?: string
  /** Temporary string for items not yet in locale files; removed in Step 4 */
  label?: string
}

export type NavGroup = {
  /** i18n key for the group heading; undefined = ungrouped (no heading in Commit A) */
  groupKey?: string
  items: NavItem[]
}

export const NAV_GROUPS: NavGroup[] = [
  {
    items: [
      { to: '/', icon: LayoutDashboard, i18nKey: 'nav.overview' },
    ],
  },
  {
    groupKey: 'nav.group.planning',
    items: [
      { to: '/shifts',     icon: ClipboardList, i18nKey: 'nav.shifts' },
      { to: '/wic-shifts', icon: Building2,     i18nKey: 'nav.wicShifts' },
      { to: '/vwic',       icon: Headphones,    i18nKey: 'nav.vwic' },
      { to: '/breaks',     icon: Coffee,        i18nKey: 'nav.breaks' },
      { to: '/training',   icon: Users,         i18nKey: 'nav.training' },
      { to: '/pipeline',   icon: ClipboardList, i18nKey: 'nav.pipeline' },
    ],
  },
  {
    groupKey: 'nav.group.wicOps',
    items: [
      { to: '/wic-attendance', icon: Calendar,    i18nKey: 'nav.wicAttendance' },
      { to: '/wic-schedule',   icon: MapPin,      i18nKey: 'nav.wicSchedule' },
      { to: '/wic',            icon: MapPin,      i18nKey: 'nav.wicLocations' },
      { to: '/wic-coverage',   icon: ShieldCheck, i18nKey: 'nav.wicCoverage' },
      { to: '/wic-al',         icon: CalendarOff, i18nKey: 'nav.wicAnnualLeave' },
    ],
  },
  {
    groupKey: 'nav.group.absence',
    items: [
      { to: '/attendance', icon: Calendar, i18nKey: 'nav.attendance' },
      { to: '/sickleave',  icon: Heart,    i18nKey: 'nav.sickLeave' },
      { to: '/vacations',  icon: Calendar, i18nKey: 'nav.vacations' },
      { to: '/albalance',  icon: Scale,    i18nKey: 'nav.alBalance' },
      { to: '/alcalendar', icon: Calendar, i18nKey: 'nav.alCalendar' },
    ],
  },
  {
    groupKey: 'nav.group.people',
    items: [
      { to: '/employees', icon: Users, i18nKey: 'nav.employees' },
    ],
  },
  {
    groupKey: 'nav.group.tools',
    items: [
      { to: '/assistant', icon: Bot,        i18nKey: 'nav.assistant' },
      { to: '/bo-list',   icon: ListChecks, i18nKey: 'nav.boList' },
      { to: '/bulk-rtm',  icon: FileText,   i18nKey: 'nav.bulkRtm' },
    ],
  },
]
