import { type ButtonHTMLAttributes, type ReactNode } from 'react'

type ButtonVariant = 'default' | 'ghost' | 'outline'
type ButtonSize = 'sm' | 'md'

const VARIANT: Record<ButtonVariant, string> = {
  default: 'bg-action text-action-fg hover:bg-action-hover border border-transparent',
  ghost:   'bg-transparent text-ink-muted hover:bg-hovered border border-transparent',
  outline: 'bg-transparent text-ink hover:bg-hovered border border-line-default hover:border-line-strong',
}

const SIZE: Record<ButtonSize, string> = {
  sm: 'h-7 px-2.5 text-xs rounded-xs',
  md: 'h-9 px-4 text-sm rounded-sm',
}

interface ButtonProps extends ButtonHTMLAttributes<HTMLButtonElement> {
  variant?: ButtonVariant
  size?: ButtonSize
  children: ReactNode
}

export function Button({ variant = 'default', size = 'md', className, children, ...rest }: ButtonProps) {
  return (
    <button
      className={[
        'inline-flex items-center justify-center font-medium transition-colors duration-fast',
        'focus-visible:outline-2 disabled:opacity-50 disabled:cursor-not-allowed',
        VARIANT[variant],
        SIZE[size],
        className ?? '',
      ].filter(Boolean).join(' ')}
      {...rest}
    >
      {children}
    </button>
  )
}
