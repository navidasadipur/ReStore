import { Alert, Button, Paper, Stack, Typography } from "@mui/material";
import { useEffect, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import agent from "../../app/api/agent";
import { getErrorMessages } from "../../app/api/contracts";

type Result = Awaited<ReturnType<typeof agent.Payments.verifyZarinpal>>;

export default function PaymentResultPage() {
    const [params] = useSearchParams();
    const authority = params.get('Authority');
    const status = params.get('Status');
    const [result, setResult] = useState<Result>();
    const [error, setError] = useState<string>();
    const [retry, setRetry] = useState(0);
    const request = useRef<{ key: string; promise: Promise<Result> }>();

    useEffect(() => {
        setResult(undefined);
        setError(undefined);
        if (!authority || (status !== 'OK' && status !== 'NOK')) return;

        const key = [authority, status, retry].join(':');
        if (request.current?.key !== key) {
            request.current = { key, promise: agent.Payments.verifyZarinpal(authority, status) };
        }
        let active = true;
        request.current.promise.then(value => {
            if (active) setResult(value);
        }).catch(failure => {
            if (active) setError(getErrorMessages(failure).join(' '));
        });
        return () => { active = false; };
    }, [authority, status, retry]);

    const valid = authority && (status === 'OK' || status === 'NOK');
    return (
        <Paper variant="outlined" sx={{ p: 3, mb: 4 }}>
            <Stack spacing={2} alignItems="flex-start">
                <Typography variant="h5" component="h1">Payment result</Typography>
                {!valid ? <Alert severity="error">The payment return link is invalid.</Alert>
                    : error ? <Alert severity="error">{error}</Alert>
                    : !result ? <Typography role="status">Checking your payment…</Typography>
                    : <Alert severity={result.paid ? "success" : "warning"}>
                        {result.paid ? 'Payment confirmed.' : 'Payment was not completed.'} Order #{result.orderId}.
                        {result.referenceId != null && <> Tracking number: {result.referenceId}.</>}
                    </Alert>}
                {valid && error && <Button variant="contained" onClick={() => setRetry(value => value + 1)}>Retry verification</Button>}
                <Button component={Link} to={result?.paid ? "/orders" : "/basket"}>
                    {result?.paid ? 'View orders' : 'Back to basket'}
                </Button>
            </Stack>
        </Paper>
    );
}
