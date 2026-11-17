import { Paper, Table, TableBody, TableCell, TableContainer, TableRow } from "@mui/material";
import { useAppSelector } from "../../app/store/configureStore";
import { currencyFormat, getDeliveryFee, freeDeliveryThresholdInRials } from "../../app/util/util";

interface Props{
    subtotal?: number;
    deliveryFee?: number;
}

export default function BasketSummary({subtotal, deliveryFee: savedDeliveryFee}: Props) {
    const {basket} = useAppSelector(state => state.basket);
    if (subtotal === undefined)
        subtotal = basket?.items.reduce((sum, item) => sum + item.quantity * item.price, 0) ?? 0;
    const deliveryFee = savedDeliveryFee ?? getDeliveryFee(subtotal);

    return (
        <>
            <TableContainer component={Paper} variant={'outlined'}>
                <Table>
                    <TableBody>
                        <TableRow>
                            <TableCell colSpan={2}>Subtotal</TableCell>
                            <TableCell align="right">{currencyFormat(subtotal)}</TableCell>
                        </TableRow>
                        <TableRow>
                            <TableCell colSpan={2}>Delivery fee*</TableCell>
                            <TableCell align="right">{currencyFormat(deliveryFee)}</TableCell>
                        </TableRow>
                        <TableRow>
                            <TableCell colSpan={2}>Total</TableCell>
                            <TableCell align="right">{currencyFormat(subtotal + deliveryFee)}</TableCell>
                        </TableRow>
                        {savedDeliveryFee === undefined && <TableRow>
                            <TableCell colSpan={3} sx={{ fontStyle: 'italic' }}>
                                ارسال رایگان برای سفارش‌های بیشتر از {currencyFormat(freeDeliveryThresholdInRials)}
                            </TableCell>
                        </TableRow>}
                    </TableBody>
                </Table>
            </TableContainer>
        </>
    )
}