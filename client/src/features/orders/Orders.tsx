import { Alert, Table, TableHead, TableRow, TableCell, TableBody, TableContainer, Paper, Button } from "@mui/material"
import AppPagination from "../../app/components/AppPagination";
import { MetaData } from "../../app/models/pagination";
import { getErrorMessages } from "../../app/api/contracts";
import { useEffect, useState } from "react";
import agent from "../../app/api/agent";
import LoadingComponent from "../../app/layout/LoadingComponent";
import OrderDetailed from "./OrderDetailed"
import { Order } from "../../app/models/order";
import { currencyFormat } from "../../app/util/util";

export default function Orders() {
  const [orders, setOrders] = useState<Order[] | null>(null);
  const [loading, setLoading] = useState(true);
  const [selectedOrder, setSelectedOrder] = useState<Order | null>(null);
  const [metaData, setMetaData] = useState<MetaData | null>(null);
  const [page, setPage] = useState(1);
  const [retry, setRetry] = useState(0);
  const [error, setError] = useState('');

  useEffect(() => {
    let active = true;
    setLoading(true);
    setError('');
    agent.Orders.list(page)
      .then(response => { if (active) { setOrders(response.items); setMetaData(response.metaData); } })
      .catch(error => { if (active) setError(getErrorMessages(error).join('\n')); })
      .finally(() => { if (active) setLoading(false); });
    return () => { active = false; };
  }, [page, retry]);

  async function selectOrder(id: number) {
    setLoading(true);
    setError('');
    try { setSelectedOrder(await agent.Orders.fetch(id)); }
    catch (error) { setError(getErrorMessages(error).join('\n')); }
    finally { setLoading(false); }
  }

  if (loading) return <LoadingComponent message='Loading orders...' />

  if (error) return <Alert severity="error" action={<Button onClick={() => setRetry(value => value + 1)}>Retry</Button>}>{error}</Alert>;
  if (selectedOrder) return (
    <OrderDetailed
      order={selectedOrder}
      setSelectedOrder={() => setSelectedOrder(null)}
    />
  )

  return (
    <>
    <TableContainer component={Paper}>
      <Table sx={{ minWidth: 650 }} aria-label="simple table">
        <TableHead>
          <TableRow>
            <TableCell>Order number</TableCell>
            <TableCell align="right">Total</TableCell>
            <TableCell align="right">Order Date</TableCell>
            <TableCell align="right">Order Status</TableCell>
            <TableCell align="right"></TableCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {orders?.map((order) => (
            <TableRow
              key={order.id}
              sx={{ '&:last-child td, &:last-child th': { border: 0 } }}
            >
              <TableCell component="th" scope="row">
                {order.id}
              </TableCell>
              <TableCell align="right">{currencyFormat(order.total)}</TableCell>
              <TableCell align="right">{order.orderDate.split('T')[0]}</TableCell>
              <TableCell align="right">{order.orderStatus}</TableCell>
              <TableCell align="right">
                <Button onClick={() => selectOrder(order.id)}>
                  View
                </Button>
              </TableCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </TableContainer>
    {metaData && <AppPagination metaData={metaData} onPageChange={setPage} />}
    </>
  )
}
