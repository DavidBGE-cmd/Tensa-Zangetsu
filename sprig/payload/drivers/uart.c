#include <mmio.h>
#include <uart.h>

static inline void uart_write(uint32_t offset, uint32_t value)
{
    writel(value, MTK_UART_BASE + offset);
}

static inline uint32_t uart_read(uint32_t offset)
{
    return readl(MTK_UART_BASE + offset);
}

void mtk_uart_putc(int ch)
{
    while (!(uart_read(MTK_UART_LSR) & MTK_UART_LSR_THRE))
        ;

    uart_write(MTK_UART_THR, ch);
}
